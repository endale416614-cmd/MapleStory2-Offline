using System.Diagnostics;

namespace MapleStory2Offline;

/// <summary>
/// Entry point. Player actions (each also a Start menu shortcut):
///   (no arguments)          play; or, when this exe carries the program package, run Setup
///   --export-diagnostics    save a diagnostics zip on the desktop (works when the game cannot start)
///   --install-update        pick a downloaded update zip and install it
///   --uninstall             remove the program (asks about saves)
/// Helpers: --install-patch/--remove-patch &lt;client dir&gt; (run elevated by the launcher), --report, and for automated
/// tests --setup-silent --dir &lt;folder&gt; --data &lt;folder&gt; [--no-shortcuts] [--no-registry], --apply-updates,
/// --export-diagnostics --quiet [--include-save], --uninstall --quiet [--delete-data].
/// Silent modes write their outcome to "last-result.txt" in the data (or target) folder and set the exit code.
/// </summary>
internal static class Program {
    private const string Title = "MapleStory 2 (Offline)";

    [STAThread]
    private static int Main(string[] args) {
        ApplicationConfiguration.Initialize();
        string exe = Environment.ProcessPath!;
        bool quiet = Has(args, "--quiet");

        if (Has(args, "--setup-silent")) {
            return SetupSilent(exe, args);
        }
        if (args.Length == 0 && Setup.FindPayload(exe) is { } payload) {
            Setup.RunInteractive(exe, payload);
            return 0;
        }
        if (args.Length == 2 && args[0] is "--install-patch" or "--remove-patch") {
            // Started elevated by the launcher when the game folder is write-protected.
            string patchDir = Settings.Load().PatchDir;
            return (args[0] == "--install-patch" ? ClientLocator.InstallPatch(args[1], patchDir) : ClientLocator.RemovePatch(args[1], patchDir)) ? 0 : 1;
        }

        Settings? settings = null;
        try {
            settings = Settings.Load();
            EventLog.Init(settings);
            if (Has(args, "--export-diagnostics")) {
                return ExportDiagnostics(settings, quiet, Has(args, "--include-save"), Value(args, "--to"));
            }
            if (Has(args, "--report")) {
                MakeReportNow(settings);
                return 0;
            }
            if (Has(args, "--uninstall")) {
                Setup.Uninstall(settings, quiet, Has(args, "--delete-data"));
                return 0;
            }
            if (Has(args, "--install-update")) {
                return InstallUpdate(settings, quiet, Value(args, "--install-update"));
            }
            if (Has(args, "--apply-updates")) {
                return ApplyUpdatesQuiet(settings);
            }
            if (Has(args, "--rollback-update")) {
                // Support aid (also offered automatically when an update fails to start): undo the last update.
                if (!quiet && MessageBox.Show("Go back to the version before the last update? Your save from before that update is restored too.",
                        Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) {
                    return 1;
                }
                try {
                    string message = new UpdateInstaller(settings).RollBackLastUpdate();
                    WriteResult(settings.DataRoot, message);
                    if (!quiet) {
                        MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                } catch (Exception ex) {
                    WriteResult(settings.DataRoot, "FAILED: " + ex.Message);
                    return 1;
                }
            }
            if (Has(args, "--check-ports")) {
                // Test/support aid: who holds the ports the servers need.
                List<PortGuard.Listener> listeners = PortGuard.Listeners();
                WriteResult(settings.DataRoot, listeners.Count == 0 ? "ports free" : string.Join(Environment.NewLine,
                    listeners.Select(l => $"port {l.Port}: {l.Name} pid {l.ProcessId} {(PortGuard.IsOwnServer(l, settings) ? "OWN" : "FOREIGN")}")));
                return listeners.Any(l => !PortGuard.IsOwnServer(l, settings)) ? 1 : 0;
            }
        } catch (Exception ex) {
            ReportCrash(settings, ex);
            return 1;
        }

        using var mutex = new Mutex(true, "MapleStory2Offline.Launcher", out bool first);
        if (!first) {
            MessageBox.Show("MapleStory 2 (Offline) is already running.", "MapleStory 2", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(settings, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(settings, e.ExceptionObject as Exception);
        try {
            Application.Run(new LauncherForm(settings));
        } catch (Exception ex) {
            ReportCrash(settings, ex);
        }
        return 0;
    }

    private static int ExportDiagnostics(Settings settings, bool quiet, bool includeSave, string? folder) {
        if (!quiet) {
            includeSave = MessageBox.Show(
                "A diagnostics file will be saved on your desktop. It contains logs and information about the game and your PC " +
                "(your Windows user name and PC name are removed).\n\nAlso include your save file? It helps to reproduce problems but contains your characters.",
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        string? path = Diagnostics.Export(settings, includeSave, folder);
        WriteResult(settings.DataRoot, path == null ? "FAILED" : "exported " + path);
        if (!quiet) {
            if (path != null) {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
                MessageBox.Show($"Saved:\n{path}\n\nSend this file with your bug report.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            } else {
                MessageBox.Show("The diagnostics file could not be written.", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        return path == null ? 1 : 0;
    }

    /// <summary>"Install Update": pick the zip (or pass its path), copy it to the updates folder, install it.</summary>
    private static int InstallUpdate(Settings settings, bool quiet, string? zip) {
        if (zip == null && !quiet) {
            using var dialog = new OpenFileDialog { Title = "Choose the downloaded update", Filter = "Update (*.zip)|*.zip" };
            if (dialog.ShowDialog() != DialogResult.OK) {
                return 1;
            }
            zip = dialog.FileName;
        }
        if (zip == null || !File.Exists(zip)) {
            WriteResult(settings.DataRoot, "FAILED: no update file");
            return 1;
        }
        string message;
        int code;
        try {
            Directory.CreateDirectory(settings.UpdateDir);
            string copy = Path.Combine(settings.UpdateDir, Path.GetFileName(zip));
            if (!string.Equals(Path.GetFullPath(zip), copy, StringComparison.OrdinalIgnoreCase)) {
                File.Copy(zip, copy, true);
            }
            var installer = new UpdateInstaller(settings);
            if (installer.RecoverInterrupted() is { } recovered) {
                EventLog.Write("recovery", recovered);
            }
            List<UpdateInstaller.Result> results = installer.InstallPending();
            message = results.Count == 0 ? "Nothing to install: this update is already installed." : $"Updated to version {results[^1].Version}.";
            code = 0;
        } catch (Exception ex) {
            message = ex.Message;
            code = 1;
        }
        WriteResult(settings.DataRoot, (code == 0 ? "" : "FAILED: ") + message);
        if (!quiet) {
            MessageBox.Show(message, Title, MessageBoxButtons.OK, code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        return code;
    }

    private static int ApplyUpdatesQuiet(Settings settings) {
        try {
            var installer = new UpdateInstaller(settings);
            string? recovered = installer.RecoverInterrupted();
            string result = string.Join(Environment.NewLine, installer.InstallPending().Select(r => $"installed {r.Version}"));
            WriteResult(settings.DataRoot, (recovered != null ? "recovered: " + recovered + Environment.NewLine : "") +
                                           $"version {settings.Version}{Environment.NewLine}{(result.Length == 0 ? "nothing to install" : result)}");
            return 0;
        } catch (Exception ex) {
            WriteResult(settings.DataRoot, $"version {settings.Version}{Environment.NewLine}FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int SetupSilent(string exe, string[] args) {
        string dir = Value(args, "--dir") ?? Setup.DefaultProgramDir;
        string data = Value(args, "--data") ?? InstallInfo.DefaultDataDir;
        try {
            var payload = Setup.FindPayload(exe) ?? throw new InvalidOperationException("This exe carries no program package.");
            Setup.Install(exe, payload, new Setup.Options(dir, data, !Has(args, "--no-shortcuts"), !Has(args, "--no-registry")));
            WriteResult(Path.GetDirectoryName(Path.GetFullPath(dir))!, "installed " + dir);
            return 0;
        } catch (Exception ex) {
            WriteResult(Path.GetDirectoryName(Path.GetFullPath(dir))!, "FAILED: " + ex.Message);
            return 1;
        }
    }

    /// <summary>"--report": a session report from the latest logs right now, then shows it in Explorer.</summary>
    private static void MakeReportNow(Settings settings) {
        var report = new SessionReport(settings);
        report.Step("Report made by hand (--report) from the latest logs");
        string? path = report.Write();
        if (path != null) {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        } else {
            MessageBox.Show("The report could not be written.", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Last resort: the launcher itself failed. Still leave a report on the desktop.</summary>
    private static void ReportCrash(Settings? settings, Exception? ex) {
        string? path = null;
        try {
            settings ??= Settings.Load();
            EventLog.Write("crash", ex?.ToString() ?? "unknown");
            if (settings.DebugReports) {
                var report = new SessionReport(settings);
                report.Problem("The launcher crashed: " + ex);
                path = report.Write();
            }
        } catch (Exception) {
            // nothing more we can do
        }

        MessageBox.Show("MapleStory 2 (Offline) ran into an unexpected error and has to close." +
                        (path == null ? "" : $"\n\nA report was saved in the \"{SessionReport.FolderName}\" folder on your desktop. " +
                                             "Please send it to the developer."),
            Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1);
    }

    private static void WriteResult(string folder, string text) {
        try {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "last-result.txt"), text);
        } catch (Exception) {
            // best effort
        }
    }

    private static bool Has(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static string? Value(string[] args, string flag) {
        int index = Array.FindIndex(args, arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[index + 1] : null;
    }
}
