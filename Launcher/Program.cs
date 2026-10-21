using System.Diagnostics;

namespace MapleStory2Offline;

/// <summary>
/// Entry point. Player actions (each also a Start menu shortcut):
///   (no arguments)          play; or, when this exe carries the program package, run Setup
///   --export-diagnostics    save a diagnostics zip on the desktop (works when the game cannot start)
///   --report-problem        send the developer a problem report with the player's own words (when the build has a report service)
///   --install-update        pick a downloaded update zip and install it
///   --uninstall             remove the program (asks about saves)
/// Helpers: --install-patch/--remove-patch &lt;client dir&gt; (run elevated by the launcher), --apply-content-pack/--restore-content-pack &lt;client dir&gt; &lt;pack dir&gt;, --report, --triage &lt;report or log&gt;, and for automated
/// tests --setup-silent --dir &lt;folder&gt; --data &lt;folder&gt; [--no-shortcuts] [--no-registry], --apply-updates, --check-updates,
/// --export-diagnostics --quiet [--include-save], --uninstall --quiet [--delete-data], --prepare-content.
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
        if (args.Length == 3 && args[0] is "--apply-content-pack" or "--restore-content-pack") {
            // Started elevated by ContentPack.ApplyElevated when the game folder is write-protected.
            try {
                bool success = args[0] == "--apply-content-pack" 
                    ? ContentPack.Apply(args[1], args[2]) 
                    : ContentPack.Restore(args[1], args[2]);
                return success ? 0 : 1;
            } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) {
                Console.Error.WriteLine(ex.Message);
                return 2;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
        if (Has(args, "--triage")) {
            // For the maintainer: the known problems of a player's report or server log, written next to it.
            string? input = Value(args, "--triage");
            if (input == null || !File.Exists(input)) {
                return 2;
            }
            File.WriteAllText(input + ".triage.txt", Triage.Describe(File.ReadLines(input)));
            return 0;
        }

        Settings? settings = null;
        try {
            settings = Settings.Load();
            EventLog.Init(settings);
            if (Has(args, "--export-diagnostics")) {
                return ExportDiagnostics(settings, quiet, Has(args, "--include-save"), Value(args, "--to"));
            }
            if (Has(args, "--report-problem")) {
                // Start menu "Report a Problem": the player's own words with a report of the latest session.
                if (ProblemReport.Url() == null) {
                    Community.ShowProblem($"This version cannot send problem reports. Please post the newest report from the \"{SessionReport.FolderName}\" " +
                                          "folder on your desktop instead.", Title, MessageBoxIcon.Information);
                    return 1;
                }
                ReportDialogs.ReportProblem(settings);
                return 0;
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
            if (Has(args, "--check-updates")) {
                return CheckUpdatesQuiet(settings);
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
            if (Has(args, "--prepare-content")) {
                // Test/support aid: the content step of Play (apply or remove the unified content, data lock) without the game.
                if (ClientLocator.Find(settings.ClientDir) is not { } client) {
                    WriteResult(settings.DataRoot, "FAILED: game folder not found");
                    return 1;
                }
                settings.ClientDir = client;
                ContentPackFlow.Result content = ContentPackFlow.Prepare(settings.UnifiedContent, settings.ClientRoot,
                    settings.ContentPackDir, settings.GameDataDir, ContentPack.ApplyElevated);
                EventLog.Write("content", $"pack={content.AppliedVersion ?? "none"} data={content.DataDir} warning={content.Warning ?? "-"} error={content.Error ?? "-"}");
                var lines = new List<string> { $"pack {content.AppliedVersion ?? "none"}", $"data {content.DataDir}" };
                if (content.Warning != null) {
                    lines.Add("warning: " + content.Warning);
                }
                lines.Add(content.Error == null ? "ok" : "FAILED: " + content.Error);
                WriteResult(settings.DataRoot, string.Join(Environment.NewLine, lines));
                return content.Error == null ? 0 : 1;
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
                Community.ShowProblem($"Saved:\n{path}\n\nAttach this file to your bug report.", Title, MessageBoxIcon.Information);
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

    /// <summary>"--check-updates": the online update check without prompts (tests and support): download + install.</summary>
    private static int CheckUpdatesQuiet(Settings settings) {
        try {
            UpdateChecker.Available? update = UpdateChecker.CheckAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
            if (update == null) {
                WriteResult(settings.DataRoot, $"version {settings.Version}{Environment.NewLine}up to date");
                return 0;
            }
            if (update.FileUrl == null) {
                WriteResult(settings.DataRoot, $"version {settings.Version}{Environment.NewLine}{update.Version} available, full Setup only");
                return 0;
            }
            UpdateChecker.DownloadAsync(update, settings, settings.UpdateDir, null, CancellationToken.None).GetAwaiter().GetResult();
            var installer = new UpdateInstaller(settings);
            installer.RecoverInterrupted();
            string installed = string.Join(Environment.NewLine, installer.InstallPending().Select(r => $"installed {r.Version}"));
            WriteResult(settings.DataRoot, $"version {settings.Version}{Environment.NewLine}{(installed.Length == 0 ? "nothing to install" : installed)}");
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
        SessionReport? report = null;
        try {
            settings ??= Settings.Load();
            EventLog.Write("crash", ex?.ToString() ?? "unknown");
            if (settings.DebugReports) {
                report = new SessionReport(settings);
                report.Problem("The launcher crashed: " + ex);
                path = report.Write();
            }
        } catch (Exception) {
            // nothing more we can do
        }

        Community.ShowProblem("MapleStory 2 (Offline) ran into an unexpected error and has to close." +
                              (path == null ? "" : $"\n\nA report was saved in the \"{SessionReport.FolderName}\" folder on your desktop. " +
                                                   "Please attach it to your report."),
            Title, MessageBoxIcon.Error);
        if (settings != null && report != null) {
            QueueCrashReport(settings, report);
        }
        Environment.Exit(1);
    }

    /// <summary>A crashing launcher cannot wait for an upload: the report is queued (asked first when SendReports=ask) and sent at the next start.</summary>
    private static void QueueCrashReport(Settings settings, SessionReport report) {
        try {
            if (ProblemReport.Url() == null || settings.SendReports == "never") {
                return;
            }
            (List<string> names, string? unreadable) = ProblemReport.Names(settings.DataRoot);
            if (unreadable != null) {
                EventLog.Write("report", "crash report not queued: could not read " + unreadable); // its character names would stay in
                return;
            }
            if (settings.SendReports == "ask" && MessageBox.Show(
                    "Send a problem report about this error to the developer? It goes out the next time the launcher starts.\n\n" + ReportDialogs.WhatIsSent,
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) {
                return;
            }
            ReportDialogs.Queue(settings, "crash", report.RawText(), null, names);
        } catch (Exception) {
            // the report stays on the desktop
        }
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
