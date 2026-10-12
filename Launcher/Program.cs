namespace MapleStory2Offline;

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        if (args.Contains("--report", StringComparer.OrdinalIgnoreCase)) {
            MakeReportNow();
            return;
        }

        using var mutex = new Mutex(true, "MapleStory2Offline.Launcher", out bool first);
        if (!first) {
            MessageBox.Show("MapleStory 2 (Offline) is already running.", "MapleStory 2", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Settings? settings = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(settings, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(settings, e.ExceptionObject as Exception);

        try {
            settings = Settings.Load();
            Application.Run(new LauncherForm(settings));
        } catch (Exception ex) {
            ReportCrash(settings, ex);
        }
    }

    /// <summary>
    /// "MapleStory 2 (Offline).exe --report": a report from the latest session's logs right now (e.g. after the PC
    /// crashed before the launcher could write one), then shows it in Explorer.
    /// </summary>
    private static void MakeReportNow() {
        ApplicationConfiguration.Initialize();
        var report = new SessionReport(Settings.Load());
        report.Step("Report made by hand (--report) from the latest logs");
        string? path = report.Write();
        if (path != null) {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        } else {
            MessageBox.Show("The report could not be written.", "MapleStory 2 (Offline)", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Last resort: the launcher itself failed. Still leave a report on the desktop.</summary>
    private static void ReportCrash(Settings? settings, Exception? ex) {
        string? path = null;
        try {
            settings ??= Settings.Load();
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
            "MapleStory 2 (Offline)", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1);
    }
}
