using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MapleStory2Offline;

/// <summary>
/// Small splash window: starts the servers, launches the game, hides while you play,
/// then waits for your character to be saved and shuts the servers down.
/// </summary>
internal sealed class LauncherForm : Form {
    private const int LOGIN_PORT = 20001;
    private const int WEB_PORT = 4000;
    private const int WORLD_GRPC_PORT = 21001;
    private const string GAME_EXE = "MapleStory2";

    private readonly Settings settings;
    private readonly ServerHost host;
    private readonly CancellationTokenSource cancel = new();
    private readonly Label status;
    private readonly SessionReport? report;
    private bool finished;

    public LauncherForm(Settings settings) {
        this.settings = settings;
        host = new ServerHost(settings);
        report = settings.DebugReports ? new SessionReport(settings) : null;

        using Stream iconStream = typeof(LauncherForm).Assembly.GetManifestResourceStream("app.ico")!;
        Icon = new Icon(iconStream);
        Text = "MapleStory 2 (Offline)";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 132);
        BackColor = Color.FromArgb(28, 30, 36);
        ForeColor = Color.White;
        TopMost = true;

        var picture = new PictureBox {
            Image = new Icon(Icon, 64, 64).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(20, 22, 64, 64),
        };
        var title = new Label {
            Text = "MapleStory 2",
            Font = new Font("Segoe UI Semibold", 16f),
            AutoSize = true,
            Location = new Point(100, 18),
        };
        var subtitle = new Label {
            Text = "Offline",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(255, 196, 92),
            AutoSize = true,
            Location = new Point(103, 50),
        };
        status = new Label {
            Text = "Starting...",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(190, 194, 204),
            AutoSize = false,
            Bounds = new Rectangle(100, 72, 320, 20),
        };
        var progress = new ProgressBar {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Bounds = new Rectangle(20, 104, 400, 8),
        };
        Controls.AddRange([picture, title, subtitle, status, progress]);
    }

    protected override async void OnShown(EventArgs e) {
        base.OnShown(e);
        await RunAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (!finished) {
            cancel.Cancel(); // Alt+F4 during start-up: stop what was started
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            host.Dispose();
            cancel.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task RunAsync() {
        CancellationToken token = cancel.Token;
        string? error = null;
        try {
            if (Process.GetProcessesByName(GAME_EXE).Length > 0) {
                throw new InvalidOperationException("MapleStory 2 is already running.");
            }

            // Servers someone started by hand (Start-Server.bat) are reused and left running.
            bool ownServers = !ServerHost.IsListening(LOGIN_PORT);
            if (ownServers) {
                SetStatus("Backing up your save...");
                await Task.Run(BackupSave, token);
                host.PrepareLogs();
                SetStatus("Starting the world...");
                host.Start("World");
                await host.WaitAsync(() => ServerHost.IsListening(WORLD_GRPC_PORT), "the world server", TimeSpan.FromMinutes(2), token);

                SetStatus("Starting the login server...");
                host.Start("Login");
                host.Start("Web");
                await host.WaitAsync(() => ServerHost.IsListening(LOGIN_PORT) && ServerHost.IsListening(WEB_PORT),
                    "the login server", TimeSpan.FromMinutes(2), token);

                SetStatus("Loading Maple World...");
                host.Start("Game");
                await host.WaitAsync(() => host.LogContains("World", "has become active"), "the game server", TimeSpan.FromMinutes(4), token);
                report?.Step("Servers started");
            } else {
                report?.Step("Servers were already running (started by hand); their logs are not from this session");
            }

            SetStatus("Launching MapleStory 2...");
            RefreshClientPatch();
            SetTestKitMod(settings.TestKit);
            using Process game = LaunchGame();
            report?.Step("Game started");
            await WaitForWindowAsync(game, token);
            report?.Step(game.HasExited ? "Game closed before its window opened" : "Game window opened");
            Hide();

            // The game may hand off to helper processes, so wait for every MapleStory2.exe to close.
            DateTime lastSeenRunning = DateTime.Now;
            var reportedStops = new HashSet<string>();
            while (Process.GetProcessesByName(GAME_EXE).Length > 0) {
                lastSeenRunning = DateTime.Now;
                foreach (string server in host.StoppedServers().Where(reportedStops.Add)) {
                    report?.Problem($"The {server} server stopped while the game was running");
                }
                await Task.Delay(1000);
            }
            report?.GameExited(game.HasExited ? game.ExitCode : null);

            if (ownServers) {
                Show();
                SetStatus("Saving your progress...");
                bool? saved = await WaitForSaveAsync(lastSeenRunning);
                if (saved == true) {
                    report?.Step("Progress saved");
                } else if (saved == false) {
                    report?.Problem("Could not confirm that the character was saved after the game closed");
                } else {
                    report?.Step("Closed before entering the world (nothing to save)");
                }
                SetStatus("Closing...");
                host.StopAll();
            }
        } catch (OperationCanceledException) {
            report?.Step("Start-up cancelled");
            host.StopAll();
        } catch (Exception ex) {
            report?.Problem(ex.Message);
            host.StopAll();
            error = ex.Message;
        }

        string? reportPath = null;
        if (report != null) {
            SetStatus("Writing the session report...");
            reportPath = await Task.Run(report.Write);
        }

        Hide();
        if (error != null) {
            MessageBox.Show(error + ReportHint(reportPath), "MapleStory 2 (Offline)", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } else if (report is { HasProblems: true } && reportPath != null) {
            MessageBox.Show("Something went wrong during this session." + ReportHint(reportPath),
                "MapleStory 2 (Offline)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        finished = true;
        Close();
    }

    private static string ReportHint(string? reportPath) => reportPath == null ? "" :
        $"\n\nA report was saved in the \"{SessionReport.FolderName}\" folder on your desktop:\n{Path.GetFileName(reportPath)}\n\n" +
        "Please send that file to the developer so the problem can be fixed.";

    private void SetStatus(string text) => status.Text = text;

    /// <summary>
    /// Consistent copy of the save (SQLite online backup) before the servers open it; keeps the newest few.
    /// </summary>
    private void BackupSave() {
        if (!File.Exists(settings.SaveFile) || settings.BackupsToKeep <= 0) {
            return;
        }

        Directory.CreateDirectory(settings.BackupDir);
        string target = Path.Combine(settings.BackupDir, $"game-server-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        using (var source = new SqliteConnection($"Data Source={settings.SaveFile}"))
        using (var destination = new SqliteConnection($"Data Source={target};Pooling=False")) {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        SqliteConnection.ClearAllPools();

        foreach (FileInfo old in new DirectoryInfo(settings.BackupDir).GetFiles("game-server-*.db")
                     .OrderByDescending(file => file.Name).Skip(settings.BackupsToKeep)) {
            old.Delete();
        }
    }

    /// <summary>Copies the rebuilt client patch into the game folder when it changed.</summary>
    private void RefreshClientPatch() {
        if (!File.Exists(Path.Combine(settings.ClientDir, "NxCharacter64.dll.bak"))) {
            throw new InvalidOperationException(
                $"The client patch is not installed in:\n{settings.ClientDir}\n\nWithout it the game tries to reach Nexon's closed servers.");
        }

        foreach (string dll in new[] { "AgarciumClient.dll", "NxCharacter64.dll" }) {
            string source = Path.Combine(settings.PatchDir, dll);
            string target = Path.Combine(settings.ClientDir, dll);
            if (File.Exists(source) && (!File.Exists(target) || !File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target)))) {
                File.Copy(source, target, true);
            }
        }
    }

    /// <summary>The test-kit client mod is only loaded while TestKit=true (mod.json vs mod.json.disabled).</summary>
    private void SetTestKitMod(bool enabled) {
        string folder = Path.Combine(settings.ModsDir, "offline-testkit");
        string on = Path.Combine(folder, "mod.json"), off = Path.Combine(folder, "mod.json.disabled");
        if (enabled && File.Exists(off) && !File.Exists(on)) {
            File.Move(off, on);
        } else if (!enabled && File.Exists(on)) {
            File.Move(on, off, true);
        }
    }

    private Process LaunchGame() {
        var info = new ProcessStartInfo(Path.Combine(settings.ClientDir, "MapleStory2.exe")) {
            WorkingDirectory = settings.ClientDir,
            UseShellExecute = false,
        };
        foreach (string arg in new[] {
                     "--nxapp=nxl", "--ip=127.0.0.1", $"--port={LOGIN_PORT}",
                     $"--username={settings.Username}", $"--password={settings.Password}",
                     "--title=MapleStory 2 (Offline)", "--autologin",
                     $"--mods={settings.ModsDir}",
                 }) {
            info.ArgumentList.Add(arg);
        }
        return Process.Start(info) ?? throw new InvalidOperationException("MapleStory 2 could not be started.");
    }

    private static async Task WaitForWindowAsync(Process game, CancellationToken token) {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(90)) {
            game.Refresh();
            if (game.HasExited || game.MainWindowHandle != IntPtr.Zero) {
                return;
            }
            await Task.Delay(500, token);
        }
    }

    /// <summary>
    /// The game server saves your character when the client disconnects. Wait until that save
    /// (character LastModified after the game closed) is in the save file before stopping it.
    /// </summary>
    /// <returns>true when saved, false when the save could not be confirmed, null when there was nothing to save.</returns>
    private async Task<bool?> WaitForSaveAsync(DateTime threshold) {
        // The game server logs this when the client drops, right before it saves the character.
        var stopwatch = Stopwatch.StartNew();
        while (!host.LogContains("Game", "Disconnected GameSession")) {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10)) {
                return null; // never entered the world (closed at character select): nothing to save
            }
            await Task.Delay(500);
        }

        stopwatch.Restart();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30)) {
            if (LastCharacterSave() is { } saved && saved >= threshold) {
                await Task.Delay(1500); // let the rest of the logout work finish
                return true;
            }
            await Task.Delay(500);
        }
        return false;
    }

    private DateTime? LastCharacterSave() {
        try {
            using var connection = new SqliteConnection($"Data Source={settings.SaveFile};Mode=ReadOnly");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(LastModified) FROM \"character\"";
            return command.ExecuteScalar() is string text &&
                   DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value)
                ? value
                : null;
        } catch (SqliteException) {
            return null; // busy or locked: try again
        }
    }
}
