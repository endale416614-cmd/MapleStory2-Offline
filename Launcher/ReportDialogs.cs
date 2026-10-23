using System.Diagnostics;

namespace MapleStory2Offline;

/// <summary>
/// The player's side of problem reports (docs/engineering/DIAGNOSTIC_REPORTS.md): by default a report goes automatically
/// once the one-time notice (ShowReportNotice) was shown; otherwise it leaves the PC only after "Send", the "send
/// automatically" checkbox, or the player's own Report a problem. The notice and each of these first say what a report
/// contains. Reports are queued (ProblemReport) and sent right away or at a later start.
/// </summary>
internal static class ReportDialogs {
    private const string Title = "MapleStory 2 (Offline)";
    private static readonly SemaphoreSlim Sending = new(1, 1);

    /// <summary>What a report holds and what never leaves the PC; the player page says the same.</summary>
    public const string WhatIsSent =
        "A report contains:\n" +
        "• What went wrong: error messages, and the last lines of the game servers' logs and map scripts (the maps, monsters, items and quests of your session)\n" +
        "• When you played: the session's date and times, and the launcher's steps\n" +
        "• Your MapleStory 2 Offline and content pack versions, your launcher settings, install folders and save size\n" +
        "• Your Windows version and basic PC details: processor, graphics card and driver, memory, screen size, free disk space\n" +
        "• If the game crashed: which part of it crashed (program and module names, versions, error code and address from Windows' event log)\n" +
        "• Your last character's level, job and map (not its name)\n" +
        "• Which online addresses the game tried to reach (they are blocked offline)\n" +
        "• A random install number, so reports from the same PC can be grouped\n\n" +
        "Never sent:\n" +
        "• Your Windows user name and PC name\n" +
        "• Your character, account and save names, and your chat\n" +
        "• What the game client itself writes (its log and its messages)\n" +
        "• Your save file and your game files\n\n" +
        "Reports go only to the MapleStory 2 Offline developer and are used only to find and fix bugs.";

    /// <summary>The one-time notice's text (1.2.1).</summary>
    public const string ReportNoticeText =
        "From this version, when a session ends with a problem (a crash, a stuck loading screen, server errors) or the launcher itself fails, its report goes to the developer without asking.\n\n" +
        "Every report helps the game's development: it shows exactly what broke and where, so fixes reach every player sooner. Keeping reports on is the easiest way to contribute to MapleStory 2 Offline. Want to do more? Share bugs and ideas on <a href=\"https://discord.gg/bEEc9QsRx\">Discord</a>, or contribute on <a href=\"https://github.com/endale416614-cmd/MapleStory2-Offline\">GitHub</a>.\n\n" +
        "Details: <a href=\"https://github.com/endale416614-cmd/MapleStory2-Offline#problem-reports\">github.com/endale416614-cmd/MapleStory2-Offline#problem-reports</a>\n\n" +
        WhatIsSent + "\n\n" +
        "You can change this any time with the \"Send problem reports to the developer automatically\" checkbox in the \"Choose how to play\" window.";

    /// <summary>"What is sent?" next to the checkbox and in Report a problem.</summary>
    public static void ShowWhatIsSent(IWin32Window owner) {
        TaskDialog.ShowDialog(owner, new TaskDialogPage {
            Caption = Title,
            Heading = "What a problem report contains",
            Text = WhatIsSent,
            Icon = TaskDialogIcon.Information,
        });
    }

    /// <summary>The "send automatically" checkbox was ticked: says what is sent first; true when the player turns it on.</summary>
    public static bool ConfirmAutomatic(IWin32Window owner) {
        var turnOn = new TaskDialogButton("Turn on");
        TaskDialogButton cancel = TaskDialogButton.Cancel;
        var page = new TaskDialogPage {
            Caption = Title,
            Heading = "Send problem reports automatically?",
            Text = "When a session ends with a problem, or the launcher itself fails, its report goes to the developer without " +
                   "asking. You can untick this any time.\n\n" + WhatIsSent,
            Icon = TaskDialogIcon.Information,
            Buttons = { turnOn, cancel },
            DefaultButton = cancel,
        };
        return TaskDialog.ShowDialog(owner, page) == turnOn;
    }

    /// <summary>The one-time notice (1.2.1): reports now go automatically; saves the answer and ReportNotice.</summary>
    public static void ShowReportNotice(IWin32Window? owner, Settings settings) {
        var keep = new TaskDialogButton("Keep it on");
        var ask = new TaskDialogButton("Ask me each time");
        var page = new TaskDialogPage {
            Caption = Title,
            Heading = "Problem reports are now sent automatically",
            Text = ReportNoticeText,
            Icon = TaskDialogIcon.Information,
            Buttons = { keep, ask },
            DefaultButton = keep,
            EnableLinks = true,
        };
        page.LinkClicked += (_, e) => {
            try {
                Process.Start(new ProcessStartInfo(e.LinkHref) { UseShellExecute = true });
            } catch (Exception) {
                // no browser: the notice can still be answered
            }
        };
        // Only the button turns sending on: a notice closed any other way means asking.
        bool on = (owner == null ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(owner, page)) == keep;
        settings.SendReports = on ? "always" : "ask";
        try {
            settings.SaveUserValue("SendReports", settings.SendReports);
            settings.SaveUserValue("ReportNotice", Settings.ReportNoticeVersion.ToString());
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // the answer counts for this run; launcher.ini could not be written, so the notice comes again next time
        }
    }

    /// <summary>After a session with a problem (SendReports=ask): true = send. "Do this every time" stores always or never.</summary>
    public static bool AskToSend(Settings settings, string uploadText) {
        var send = new TaskDialogButton("Send");
        var dontSend = new TaskDialogButton("Don't send");
        var remember = new TaskDialogVerificationCheckBox("Do this every time (no more questions)");
        var page = new TaskDialogPage {
            Caption = Title,
            Heading = "Send a problem report to the developer?",
            Text = "Something went wrong in this session. A report helps find and fix it.\n\n" + WhatIsSent +
                   "\n\n<a href=\"view\">See exactly what would be sent</a>",
            EnableLinks = true,
            Icon = TaskDialogIcon.Information,
            Buttons = { send, dontSend },
            DefaultButton = dontSend,
            Verification = remember,
        };
        page.LinkClicked += (_, _) => Preview(settings, uploadText);
        bool yes = TaskDialog.ShowDialog(page) == send;
        if (remember.Checked) {
            settings.SendReports = yes ? "always" : "never";
            try {
                settings.SaveUserValue("SendReports", settings.SendReports);
                if (!yes) {
                    ProblemReport.Withdraw(settings.ReportQueueDir, "problem", "crash");
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // the answer still counts for this report; launcher.ini could not be written
            }
        }
        return yes;
    }

    /// <summary>Report a problem from the Start menu (--report-problem): waits while sending, as there is no window to return to.</summary>
    public static void ReportProblem(Settings settings) {
        try {
            if (AskAndQueue(null, settings) is not { } queued) {
                return;
            }
            var sent = new HashSet<string>();
            Cursor.Current = Cursors.WaitCursor;
            // On a pool thread: this Main has no message loop for an await to come back to.
            Task.Run(() => SendQueuedAsync(settings, sent)).GetAwaiter().GetResult();
            Cursor.Current = Cursors.Default;
            ShowResult(null, queued, sent);
        } catch (Exception ex) {
            Cursor.Current = Cursors.Default;
            MessageBox.Show("The report could not be made: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Report a problem from the "Choose how to play" window, which stays responsive while the report is sent.</summary>
    public static async void ReportProblemFrom(IWin32Window owner, Settings settings) {
        try {
            if (AskAndQueue(owner, settings) is not { } queued) {
                return;
            }
            var sent = new HashSet<string>();
            await SendQueuedAsync(settings, sent);
            ShowResult(Alive(owner), queued, sent);
        } catch (Exception ex) {
            MessageBox.Show(Alive(owner), "The report could not be made: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // The player may press Play while a report is sent: the chooser is then closed and cannot own a message box.
    private static IWin32Window? Alive(IWin32Window owner) => owner is Control { IsDisposed: true } ? null : owner;

    /// <summary>
    /// Queues a report from the unredacted session report (SessionReport.RawText); the next SendQueuedAsync sends it.
    /// Returns its file. The names must be complete (ProblemReport.Names).
    /// </summary>
    public static string Queue(Settings settings, string kind, string rawReport, string? note, List<string> names) {
        Guid id = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        return ProblemReport.Enqueue(settings.ReportQueueDir, id, kind,
            ProblemReport.Build(id, now, kind, settings.InstallId, "windows", settings.Version, rawReport, note, names), now);
    }

    /// <summary>
    /// Sends the queued reports (10 s each at most), one sending at a time; when the player chose never, the queued
    /// automatic reports are taken back first (a manual report still goes). Never throws. Returns the number sent.
    /// </summary>
    public static async Task<int> SendQueuedAsync(Settings settings, ISet<string>? sent = null) {
        await Sending.WaitAsync();
        try {
            if (settings.SendReports == "never") {
                try {
                    ProblemReport.Withdraw(settings.ReportQueueDir, "problem", "crash");
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                    EventLog.Write("report", "queued reports not withdrawn: " + ex.Message);
                }
            }
            using var handler = new HttpClientHandler();
            int count = await ProblemReport.SendAsync(settings.ReportQueueDir, ProblemReport.Url(), handler, DateTime.UtcNow, CancellationToken.None, sent);
            if (count > 0) {
                EventLog.Write("report", $"{count} problem report(s) sent");
            }
            return count;
        } catch (Exception ex) {
            EventLog.Write("report", "problem reports not sent: " + ex.Message);
            return 0;
        } finally {
            Sending.Release();
        }
    }

    // The Report a problem window; the queued file, or null when cancelled or when a save could not be read.
    private static string? AskAndQueue(IWin32Window? owner, Settings settings) {
        var report = new SessionReport(settings);
        report.Step("Report a problem (sent by the player)");
        string raw = report.RawText();
        (List<string> names, string? unreadable) = ProblemReport.Names(settings.DataRoot);
        if (unreadable != null) {
            MessageBox.Show(owner, UnreadableSave(settings, unreadable) + " If the game is running, close it and try again; otherwise " +
                                   "please report the problem on Discord.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        using var form = new Form {
            Text = "Report a problem",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = owner == null,
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            TopMost = true,
            AutoScaleMode = AutoScaleMode.Font,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14) };
        layout.Controls.Add(new Label {
            Text = "What happened? A few words help the developer find it (optional).",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
        });
        var note = new TextBox {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = ProblemReport.MaxNoteChars,
            Size = new Size(460, 120),
            Margin = new Padding(0, 6, 0, 6),
        };
        layout.Controls.Add(note);
        layout.Controls.Add(new Label {
            Text = "Your note goes with a report of your latest session: error messages, versions and basic PC details. " +
                   "Never your Windows user name, PC name, character or account names, chat or save.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
        });
        var links = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        links.Controls.Add(Link("What is sent?", () => ShowWhatIsSent(form)));
        links.Controls.Add(Link("See exactly what will be sent", () => Preview(settings,
            ProblemReport.ForUpload(raw, names) + (string.IsNullOrWhiteSpace(note.Text) ? "" : "\n\n-- Your note --\n" + ProblemReport.Note(note.Text, names)))));
        layout.Controls.Add(links);
        var send = new Button { Text = "Send", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([cancel, send]);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        form.CancelButton = cancel;
        if ((owner == null ? form.ShowDialog() : form.ShowDialog(owner)) != DialogResult.OK) {
            return null;
        }
        return Queue(settings, "manual", raw, string.IsNullOrWhiteSpace(note.Text) ? null : note.Text, names);
    }

    /// <summary>Why no report can be made while a save cannot be read (it would leave that save's character names in).</summary>
    public static string UnreadableSave(Settings settings, string unreadable) {
        string label = ProblemReport.SaveLabel(settings.DataRoot, unreadable);
        return $"No problem report could be made: the {(label is "main save" or "save folder" ? label : $"save \"{label}\"")} could not be " +
               "read (it may be in use or damaged), so its character names could not be removed from the report.";
    }

    private static void ShowResult(IWin32Window? owner, string queued, ISet<string> sent) {
        string message = sent.Contains(queued) ? "Thank you! Your report was sent to the developer."
            : File.Exists(queued) ? "Your report is saved and will be sent the next time the launcher starts with an internet connection."
            : "The report service did not take this report. Please report the problem on Discord instead.";
        MessageBox.Show(owner, message, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // The exact report text, opened in Notepad so the player can read it before deciding.
    private static void Preview(Settings settings, string text) {
        try {
            Directory.CreateDirectory(settings.ReportDir);
            string path = Path.Combine(settings.ReportDir, "to-send.txt");
            File.WriteAllText(path, text);
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        } catch (Exception) {
            // without the preview the question can still be answered
        }
    }

    private static LinkLabel Link(string text, Action click) {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
        link.LinkClicked += (_, _) => click();
        return link;
    }
}
