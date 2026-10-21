using System.Diagnostics;
using System.Text.Json;
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
    private const string Title = "MapleStory 2 (Offline)";
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SaveWait = TimeSpan.FromSeconds(60);

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
        Text = Title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 152);
        BackColor = Color.FromArgb(28, 30, 36);
        ForeColor = Color.White;

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
            Text = $"Offline  v{settings.Version}",
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
        var discord = new LinkLabel {
            Text = "Found a bug? Report it on Discord",
            Font = new Font("Segoe UI", 8.5f),
            LinkColor = Color.FromArgb(150, 170, 255),
            ActiveLinkColor = Color.White,
            AutoSize = true,
            Location = new Point(18, 124),
        };
        discord.LinkClicked += (_, _) => Community.OpenDiscord();
        Controls.AddRange([picture, title, subtitle, status, progress, discord]);

        // Borderless: drag the window by any part but the link.
        foreach (Control control in new Control[] { this, picture, title, subtitle, status, progress }) {
            control.MouseDown += DragWindow;
        }
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
        var updates = new UpdateInstaller(settings);
        string? error = null;
        try {
            EventLog.Init(settings);
            _ = ReportDialogs.SendQueuedAsync(settings); // reports the player agreed to earlier, in the background
            if (Process.GetProcessesByName(GAME_EXE).Length > 0) {
                throw new InvalidOperationException("MapleStory 2 is already running. Close it first.");
            }

            if (updates.RecoverInterrupted() is { } recovered) {
                report?.Problem(recovered);
                Hide();
                MessageBox.Show(recovered, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show();
            }
            if (settings.AutoUpdate != "off" && File.Exists(Path.Combine(settings.Root, "files.json"))) {
                await DownloadOnlineUpdateAsync(token);
            }
            if (await InstallUpdatesAsync(updates)) {
                return; // the launcher itself was updated and restarted
            }
            if (!LocateClient()) {
                report?.Step("No game folder chosen");
                finished = true;
                Close();
                return;
            }

            if (!ChooseGameMode()) {
                report?.Step("Quit at the mode choice");
                finished = true;
                Close();
                return;
            }
            ClaimPorts();

            SetStatus("Backing up your save...");
            if (await Task.Run(BackupSave, token) is { } backupProblem) {
                Hide();
                DialogResult answer = MessageBox.Show(
                    $"Your save failed its integrity check ({backupProblem}).\nStarting now may make a damaged save worse.\n\nContinue anyway?",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                Show();
                if (answer != DialogResult.Yes) {
                    throw new OperationCanceledException();
                }
            }
            SetStatus(settings.UnifiedContent ? "Preparing the unified content..." : "Checking the game files...");
            ContentPackFlow.Result content = await Task.Run(() => ContentPackFlow.Prepare(settings.UnifiedContent, settings.ClientRoot,
                settings.ContentPackDir, settings.GameDataDir, ContentPack.ApplyElevated), token);
            EventLog.Write("content", $"pack={content.AppliedVersion ?? "none"} data={content.DataDir} warning={content.Warning ?? "-"} error={content.Error ?? "-"}");
            if (content.Error != null) {
                throw new InvalidOperationException(content.Warning == null ? content.Error : content.Warning + "\n\n" + content.Error);
            }
            if (content.Warning != null) {
                report?.Problem(content.Warning);
                Hide();
                MessageBox.Show(content.Warning, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show();
            }
            host.DataDir = content.DataDir;
            host.PrepareLogs();
            Directory.CreateDirectory(settings.SaveDir); // first run: no save yet, and File.Delete throws on a missing folder
            File.Delete(host.SaveStatusFile);
            try {
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
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                host.StopAll();
                EventLog.Write("error", "servers failed to start: " + ex.Message);
                OfferRollback(updates, ex.Message);
                throw;
            }
            report?.Step("Servers started");
            EventLog.Write("servers", "started");

            SetStatus("Launching MapleStory 2...");
            InstallClientPatch();
            // Client mods follow the effective option: its own line in the ini, else the game mode's default.
            SetMod("offline-testkit", settings.TestKit);
            SetMod("offline-dungeons", settings.UnlimitedDungeonClears ?? settings.Adapted); // dungeon window shows the 9999 clear limits
            SetMod("offline-pets", settings.PetTaming ?? settings.Adapted); // taming items usable from level 10
            SetMod("offline-shop", true); // full Meret Market tabs, with the offline Meret catalog (both modes)
            using Process game = LaunchGame();
            report?.Step("Game started");
            EventLog.Write("game", "started");
            await WaitForWindowAsync(game, token);
            Hide();

            // The game may hand off to helper processes, so wait for every MapleStory2.exe to close.
            var reportedStops = new HashSet<string>();
            while (Process.GetProcessesByName(GAME_EXE).Length > 0) {
                foreach (string server in host.StoppedServers().Where(reportedStops.Add)) {
                    report?.Problem($"The {server} server stopped while the game was running");
                    EventLog.Write("error", $"{server} server stopped during play");
                }
                await Task.Delay(1000);
            }
            int? exitCode = game.HasExited ? game.ExitCode : null;
            report?.GameExited(exitCode);
            EventLog.Write("game", $"closed exit={exitCode?.ToString() ?? "?"}");

            Show();
            SetStatus("Saving your progress...");
            SaveOutcome outcome = await WaitForSaveAsync();
            switch (outcome) {
                case SaveOutcome.Committed:
                    report?.Step("Progress saved");
                    updates.MarkVerified();
                    break;
                case SaveOutcome.NothingToSave:
                    report?.Step("Closed before entering the world (nothing to save)");
                    updates.MarkVerified();
                    break;
                case SaveOutcome.Failed:
                    report?.Problem("The game server reported that saving your progress FAILED; the previous save is kept");
                    break;
                case SaveOutcome.Unconfirmed:
                    report?.Problem("The save was not confirmed; the servers were closed at your request");
                    break;
            }
            EventLog.Write("save", outcome.ToString());
            if (outcome == SaveOutcome.Failed) {
                Hide();
                Community.ShowProblem("Your progress from this session could not be saved. Your previous save was kept.\n\n" +
                                      "Please use \"MapleStory 2 Offline - Export Diagnostics\" in the Start menu and post the file with your report.",
                    Title, MessageBoxIcon.Error);
            }
            SetStatus("Closing...");
            host.StopAll();
            EventLog.Write("servers", "stopped");
            if (SaveBackup.Checkpoint(settings.SaveFile) is { } checkpointProblem) {
                EventLog.Write("save", "could not fold the write-ahead log into the save (kept beside it): " + checkpointProblem);
            }
        } catch (OperationCanceledException) {
            report?.Step("Start-up cancelled");
            EventLog.Write("cancel", "start-up cancelled");
            host.StopAll();
        } catch (Exception ex) {
            report?.Problem(ex.Message);
            EventLog.Write("error", ex.Message);
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
            Community.ShowProblem(error + ReportHint(reportPath), Title, MessageBoxIcon.Error);
        } else if (report is { HasProblems: true } && reportPath != null) {
            Community.ShowProblem("Something went wrong during this session." + ReportHint(reportPath), Title, MessageBoxIcon.Warning);
        }
        if (report is { HasProblems: true }) {
            await OfferReportAsync(report);
        }

        finished = true;
        Close();
    }

    /// <summary>A session with a problem: its report goes to the developer per SendReports ("ask" asks first, saying what is sent).</summary>
    private async Task OfferReportAsync(SessionReport sessionReport) {
        if (ProblemReport.Url() == null || settings.SendReports == "never") {
            return;
        }
        try {
            string raw = await Task.Run(sessionReport.RawText);
            (List<string> names, string? unreadable) = ProblemReport.Names(settings.DataRoot);
            if (unreadable != null) {
                EventLog.Write("report", "not offered: could not read " + unreadable);
                MessageBox.Show(ReportDialogs.UnreadableSave(settings, unreadable), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (settings.SendReports == "ask" && !ReportDialogs.AskToSend(settings, ProblemReport.ForUpload(raw, names))) {
                return;
            }
            ReportDialogs.Queue(settings, "problem", raw, null, names);
            Show();
            SetStatus("Sending the problem report...");
            await ReportDialogs.SendQueuedAsync(settings);
        } catch (Exception ex) {
            EventLog.Write("report", "problem report not queued: " + ex.Message);
        }
    }

    private static string ReportHint(string? reportPath) => reportPath == null ? "" :
        $"\n\nA report was saved in the \"{SessionReport.FolderName}\" folder on your desktop:\n{Path.GetFileName(reportPath)}\n\n" +
        "Please post that file with your report (or use \"Export Diagnostics\" in the Start menu).";

    private void SetStatus(string text) => status.Text = text;

    /// <summary>
    /// Moves the window when dragging it by any part except the Discord link.
    /// </summary>
    private void DragWindow(object? sender, MouseEventArgs e) {
        if (e.Button == MouseButtons.Left) {
            ((Control) sender!).Capture = false;
            Message move = Message.Create(Handle, 0xA1, 2, IntPtr.Zero); // WM_NCLBUTTONDOWN on the caption: Windows moves the window
            WndProc(ref move);
        }
    }

    /// <summary>
    /// Stops servers this installation left running and refuses to start when another program holds a port.
    /// Nothing outside this installation is ever terminated.
    /// </summary>
    private void ClaimPorts() {
        List<PortGuard.Listener> listeners = PortGuard.Listeners();
        List<PortGuard.Listener> foreign = listeners.Where(listener => !PortGuard.IsOwnServer(listener, settings)).ToList();
        if (foreign.Count > 0) {
            string list = string.Join("\n", foreign.Select(listener => $"  port {listener.Port}: {listener.Name} (process {listener.ProcessId})"));
            EventLog.Write("error", "ports in use: " + list.Replace("\n", ";"));
            throw new InvalidOperationException(
                $"Another program is using a network port MapleStory 2 Offline needs:\n{list}\n\n" +
                "Close that program (or another copy of MapleStory 2 Offline) and start again.");
        }
        List<PortGuard.Listener> leftovers = listeners.Where(listener => PortGuard.IsOwnServer(listener, settings)).ToList();
        if (leftovers.Count > 0) {
            SetStatus("Closing servers left over from last time...");
            EventLog.Write("startup", "stopping leftover servers of this installation: " + string.Join(", ", leftovers.Select(l => l.Name).Distinct()));
            report?.Step("Stopped servers left over from a previous run");
            PortGuard.StopOwnServers(leftovers);
        }
    }

    /// <summary>
    /// Consistent copy of the save (SQLite online backup) before the servers open it, checked with
    /// PRAGMA integrity_check; keeps the newest few. A damaged copy is deleted and reported.
    /// </summary>
    private string? BackupSave() {
        if (!File.Exists(settings.SaveFile) || settings.BackupsToKeep <= 0) {
            return null;
        }

        Directory.CreateDirectory(settings.BackupDir);
        string target = Path.Combine(settings.BackupDir, $"game-server-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        string? problem = SaveBackup.Create(settings.SaveFile, target);
        if (problem != null) {
            report?.Problem("The save backup failed its check and was discarded: " + problem);
            EventLog.Write("backup", "FAILED " + problem);
        } else {
            EventLog.Write("backup", $"ok {Path.GetFileName(target)}");
        }

        foreach (FileInfo old in new DirectoryInfo(settings.BackupDir).GetFiles("game-server-*.db")
                     .OrderByDescending(file => file.Name).Skip(settings.BackupsToKeep)) {
            old.Delete();
        }
        return problem;
    }

    /// <summary>Installs pending update zips. Returns true when the launcher restarted itself.</summary>
    private async Task<bool> InstallUpdatesAsync(UpdateInstaller installer) {
        if (!installer.Pending().Any()) {
            return false;
        }
        SetStatus("Installing update...");
        List<UpdateInstaller.Result> installed;
        try {
            installed = await Task.Run(() => installer.InstallPending(text => Invoke(() => SetStatus(text))));
        } catch (Exception ex) {
            report?.Problem(ex.Message);
            Hide();
            MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Show();
            return false; // keep playing the installed version
        }
        foreach (UpdateInstaller.Result result in installed) {
            report?.Step($"Updated to version {result.Version}");
        }
        if (installed.Count == 0) {
            return false;
        }

        string notes = string.Join("\n", installed.Where(r => !string.IsNullOrWhiteSpace(r.Notes)).Select(r => $"{r.Version}: {r.Notes}"));
        Hide();
        MessageBox.Show($"Updated to version {installed[^1].Version}." + (notes.Length > 0 ? $"\n\n{notes}" : ""),
            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (!installed.Any(r => r.LauncherReplaced)) {
            Show();
            return false;
        }

        // The launcher exe itself changed: start the new one.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, WorkingDirectory = settings.Root });
        finished = true;
        Close();
        return true;
    }

    /// <summary>
    /// AutoUpdate: when the PC is online and a newer release exists, fetch its signed update file into the updates
    /// folder (asking first unless AutoUpdate=always); <see cref="InstallUpdatesAsync"/> then verifies and installs it.
    /// Any failure here only means playing the installed version.
    /// </summary>
    private async Task DownloadOnlineUpdateAsync(CancellationToken token) {
        SetStatus("Checking for updates...");
        UpdateChecker.Available? update;
        try {
            update = await UpdateChecker.CheckAsync(settings, token);
        } catch (Exception ex) when (!token.IsCancellationRequested) {
            EventLog.Write("update", "online check skipped: " + ex.Message);
            return;
        }
        if (update == null) {
            return;
        }
        EventLog.Write("update", $"version {update.Version} available online");

        if (update.FileUrl == null) {
            // A release that can only be installed with its full Setup.
            Hide();
            bool open = MessageBox.Show($"Version {update.Version} is available (you have {settings.Version}). It has to be installed with its full Setup.\n\n" +
                                        "Open the download page?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
            Show();
            if (open && update.PageUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) {
                Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            }
            return;
        }
        if (settings.AutoUpdate == "ask") {
            Hide();
            DialogResult answer = MessageBox.Show(
                $"Version {update.Version} is available (you have {settings.Version}).\n\n" + (update.Notes.Length > 0 ? update.Notes + "\n\n" : "") +
                $"Update now? ({Math.Max(1, update.Size / 1048576)} MB download; your save is backed up first.)\n\n" +
                "To update by hand instead, set AutoUpdate=off in launcher.ini.",
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            Show();
            if (answer != DialogResult.Yes) {
                EventLog.Write("update", $"{update.Version} postponed by the player");
                return;
            }
        }
        try {
            var progress = new Progress<int>(percent => SetStatus($"Downloading update {update.Version}... {percent}%"));
            string file = await UpdateChecker.DownloadAsync(update, settings, settings.UpdateDir, progress, token);
            EventLog.Write("update", $"downloaded {Path.GetFileName(file)}");
        } catch (Exception ex) when (!token.IsCancellationRequested) {
            EventLog.Write("update", $"download of {update.Version} failed: {ex.Message}");
            Hide();
            MessageBox.Show($"The update could not be downloaded ({ex.Message}). You can keep playing; it is tried again next time.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Show();
        }
    }

    /// <summary>After an update that has not yet started successfully: offer to go back to the previous version.</summary>
    private void OfferRollback(UpdateInstaller updates, string reason) {
        if (updates.UnverifiedUpdate() is not { } last) {
            return;
        }
        Hide();
        DialogResult answer = MessageBox.Show(
            $"The game did not start after updating to version {last.Version}:\n{reason}\n\n" +
            $"Go back to version {last.PreviousVersion}? Your save from before the update is restored too.",
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes) {
            string result = updates.RollBackLastUpdate();
            report?.Step(result);
            MessageBox.Show(result, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>Finds the game (saved choice, Steam libraries) or asks the player once; remembers the answer.</summary>
    private bool LocateClient() {
        string? found = ClientLocator.Find(settings.ClientDir);
        if (found == null) {
            Hide();
            found = ClientLocator.Ask(null);
            if (found == null) {
                return false;
            }
            Show();
        }
        if (!string.Equals(found, settings.ClientDir, StringComparison.OrdinalIgnoreCase)) {
            settings.ClientDir = found;
            settings.SaveUserValue("ClientDir", found);
        }
        report?.Step($"Game folder: {found}");
        return true;
    }

    /// <summary>Installs the client patch (first run) or refreshes it after an update; asks for admin rights if needed.</summary>
    private void InstallClientPatch() {
        if (ClientLocator.PatchInstalled(settings.ClientDir, settings.PatchDir)) {
            return;
        }
        if (!ClientLocator.InstallPatch(settings.ClientDir, settings.PatchDir)) {
            SetStatus("Installing the client patch (Windows will ask for permission)...");
            ClientLocator.InstallPatchElevated(settings.ClientDir);
        }
        if (!ClientLocator.PatchInstalled(settings.ClientDir, settings.PatchDir)) {
            throw new InvalidOperationException(
                $"The offline patch could not be installed into the game folder:\n{settings.ClientDir}\n\n" +
                "Without it the game tries to reach Nexon's closed servers. Start the launcher again and allow the Windows prompt.");
        }
        report?.Step("Client patch installed");
        EventLog.Write("patch", "client patch installed into " + settings.ClientDir);
    }

    /// <summary>Every start: "Choose how to play" with the last choice selected. False when the player quits.</summary>
    private bool ChooseGameMode() {
        bool canSend = ProblemReport.Url() != null;
        using var chooser = new ModeChooser(settings.GameMode, Icon!, settings.DataRoot, settings.Profile,
            canSend ? settings.SendReports : null, canSend ? (IWin32Window owner) => ReportDialogs.ReportProblemFrom(owner, settings) : null);
        Hide();
        DialogResult result = chooser.ShowDialog();
        Show();
        if (result != DialogResult.OK) {
            return false;
        }
        if (!settings.GameModeSet || chooser.Mode != settings.GameMode) {
            settings.GameMode = chooser.Mode;
            settings.GameModeSet = true;
            settings.SaveUserValue("GameMode", settings.GameMode);
        }
        report?.Step("Game mode: " + settings.GameMode);
        if (chooser.Profile != settings.Profile) {
            settings.Profile = chooser.Profile;
            settings.SaveUserValue("Profile", settings.Profile);
        }
        report?.Step("Save profile: " + (settings.Profile.Length == 0 ? "main" : settings.Profile));
        if (chooser.SendReports is { } sendReports && sendReports != settings.SendReports) {
            settings.SendReports = sendReports;
            settings.SaveUserValue("SendReports", sendReports);
        }
        EventLog.Write("mode", settings.GameMode);
        return true;
    }

    /// <summary>A client mod that follows a setting is only loaded while it is on (mod.json vs mod.json.disabled).</summary>
    private void SetMod(string mod, bool enabled) {
        string folder = Path.Combine(settings.ModsDir, mod);
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

    /// <summary>
    /// Waits for the game window. A game that closes during start-up, or shows no window in time, is reported
    /// visibly (the player may keep waiting or stop the game this launcher started).
    /// </summary>
    private async Task WaitForWindowAsync(Process game, CancellationToken token) {
        var stopwatch = Stopwatch.StartNew();
        while (true) {
            game.Refresh();
            if (game.HasExited) {
                EventLog.Write("error", $"game closed during start-up exit={game.ExitCode}");
                throw new InvalidOperationException(
                    $"MapleStory 2 closed while starting (exit code {game.ExitCode}). The offline patch or the game files may be damaged.");
            }
            if (game.MainWindowHandle != IntPtr.Zero) {
                report?.Step("Game window opened");
                EventLog.Write("game", "window opened");
                return;
            }
            if (stopwatch.Elapsed > WindowTimeout) {
                EventLog.Write("error", "game window did not appear in time");
                Hide();
                DialogResult answer = MessageBox.Show(
                    "MapleStory 2 has not opened a window yet.\n\nKeep waiting? (No closes the game this launcher started.)",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                Show();
                if (answer == DialogResult.No) {
                    try {
                        game.Kill(entireProcessTree: true);
                    } catch (InvalidOperationException) {
                        // already closed
                    }
                    throw new TimeoutException("MapleStory 2 did not open a window and was closed.");
                }
                stopwatch.Restart();
            }
            await Task.Delay(500, token);
        }
    }

    private enum SaveOutcome { Committed, Failed, NothingToSave, Unconfirmed }

    private sealed record SaveStatus(long CharacterId, long AccountId, string Result, string? Error, string? Utc);

    /// <summary>
    /// The game server writes save-status.json: "active" once a character is in the world, then "committed" or
    /// "failed" for the save made when the client leaves. The servers keep running until the save is confirmed or
    /// the player decides to close them.
    /// </summary>
    private async Task<SaveOutcome> WaitForSaveAsync() {
        var stopwatch = Stopwatch.StartNew();
        while (true) {
            SaveStatus? state = ReadSaveStatus();
            if (state == null && stopwatch.Elapsed > TimeSpan.FromSeconds(5)) {
                return SaveOutcome.NothingToSave; // no character ever entered the world
            }
            if (state?.Result == "committed") {
                return SaveOutcome.Committed;
            }
            if (state?.Result == "failed") {
                EventLog.Write("save", $"failed character={state.CharacterId} error={state.Error}");
                return SaveOutcome.Failed;
            }
            if (host.StoppedServers().Contains("Game")) {
                EventLog.Write("save", "game server stopped before confirming the save");
                return SaveOutcome.Failed;
            }
            if (stopwatch.Elapsed > SaveWait) {
                Hide();
                DialogResult answer = MessageBox.Show(
                    "Your progress is still being saved.\n\nKeep waiting? (No closes the servers now; progress since your last save may be lost.)",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                Show();
                if (answer == DialogResult.No) {
                    return SaveOutcome.Unconfirmed;
                }
                stopwatch.Restart();
            }
            await Task.Delay(500);
        }
    }

    private SaveStatus? ReadSaveStatus() {
        try {
            if (!File.Exists(host.SaveStatusFile)) {
                return null;
            }
            using var stream = new FileStream(host.SaveStatusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<SaveStatus>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        } catch (Exception) {
            return null; // being replaced right now: read again
        }
    }
}

/// <summary>Validated SQLite backups of the save.</summary>
internal static class SaveBackup {
    /// <summary>Online backup + PRAGMA integrity_check of the copy. Returns null when good, else the problem (copy removed).</summary>
    public static string? Create(string source, string target) {
        try {
            using (var from = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
            using (var to = new SqliteConnection($"Data Source={target};Pooling=False")) {
                from.Open();
                to.Open();
                from.BackupDatabase(to);
                using SqliteCommand single = to.CreateCommand();
                single.CommandText = "PRAGMA journal_mode=DELETE"; // the copy is one self-contained file
                single.ExecuteNonQuery();
            }
            string? problem = Check(target);
            if (problem != null) {
                File.Delete(target);
            }
            return problem;
        } catch (Exception ex) {
            try {
                File.Delete(target);
            } catch (Exception) {
                // nothing to clean
            }
            return ex.Message;
        } finally {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// After the servers are stopped (killed, so SQLite could not tidy up), folds the write-ahead log back into the save
    /// so game-server.db alone holds all progress. Returns null when done or not needed, else the problem.
    /// </summary>
    public static string? Checkpoint(string path) {
        if (!File.Exists(path) || !File.Exists(path + "-wal")) {
            return null;
        }
        try {
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() && reader.GetInt32(0) != 0 ? "the save is busy" : null;
        } catch (Exception ex) {
            return ex.Message;
        } finally {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>null when the database passes integrity_check, else the first problem.</summary>
    public static string? Check(string path) {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        string result = command.ExecuteScalar() as string ?? "no result";
        return result == "ok" ? null : result;
    }
}
