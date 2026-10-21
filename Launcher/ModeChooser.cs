namespace MapleStory2Offline;

/// <summary>
/// "Choose how to play": shown at every start before the servers run, with the last choice selected. Play saves the
/// choice (GameMode in launcher.ini); Quit closes the launcher. Plain words only: players never need the ini file.
/// </summary>
internal sealed class ModeChooser : Form {
    public const string LegacyTitle = "Legacy Mode";
    public const string LegacyText =
        "The original online-era rules and balance: original exp and drop rates, dungeon clear limits and party requirements. " +
        "Nothing is changed for solo play, so content built for a party stays closed.";
    public const string AdaptedTitle = "Offline Adapted Mode";
    public const string AdaptedText =
        "Built for playing alone: faster exp and more loot, unlimited dungeon clear rewards, Epic dungeon gear, easier pet taming, " +
        "party-size dungeons open to you (except Cathy Mart, which needs a full group), bosses scaled for one player, and a Black Market with items for sale.";
    public const string FooterText =
        "Both modes keep Meret pouches, the full Meret Market, mail that never expires and the chat commands. " +
        "Your characters work in either mode. To switch, close the game and start it again.";

    private static readonly Color Back = Color.FromArgb(28, 30, 36);
    private static readonly Color Muted = Color.FromArgb(190, 194, 204);
    private static readonly Color Accent = Color.FromArgb(255, 196, 92);

    private readonly RadioButton legacy;
    private readonly RadioButton adapted;
    private readonly ComboBox profileCombo;
    private readonly Button newGameButton;
    private readonly CheckBox? autoReports;
    private readonly string sendReports;

    /// <summary>"Legacy" or "Adapted": the selected option.</summary>
    public string Mode => legacy.Checked ? "Legacy" : "Adapted";

    /// <summary>The selected profile name (empty for main save).</summary>
    public string Profile => profileCombo.SelectedIndex == 0 ? "" : profileCombo.SelectedItem?.ToString() ?? "";

    /// <summary>launcher.ini SendReports after the choice; null when there was no checkbox (nothing to save).</summary>
    public string? SendReports => autoReports == null ? null : Settings.SendReportsAfterCheckbox(sendReports, autoReports.Checked);

    /// <param name="sendReports">The SendReports setting; null when this build cannot send reports (no checkbox).</param>
    /// <param name="reportProblem">Opens Report a problem; null hides the link.</param>
    public ModeChooser(string current, Icon icon, string dataRoot, string profile, string? sendReports = null,
        Action<IWin32Window>? reportProblem = null) {
        this.sendReports = sendReports ?? "ask";
        Text = "Choose how to play";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Back;
        ForeColor = Color.White;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 16, 20, 14),
        };
        layout.Controls.Add(new Label {
            Text = "Choose how to play",
            Font = new Font("Segoe UI Semibold", 14f),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        });
        legacy = AddOption(layout, LegacyTitle, LegacyText);
        adapted = AddOption(layout, AdaptedTitle, AdaptedText);

        // Profile selection
        var profileLabel = new Label {
            Text = "Save:",
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 4),
        };
        layout.Controls.Add(profileLabel);

        profileCombo = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        // Populate the combo box with available profiles
        var profiles = new List<string> { "Main save" };
        try {
            string saveDataPath = Path.Combine(dataRoot, "SaveData");
            if (Directory.Exists(saveDataPath)) {
                var subDirs = Directory.GetDirectories(saveDataPath)
                    .Select(Path.GetFileName)
                    .Where(dir => !string.Equals(dir, "backups", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                profiles.AddRange(subDirs);
            }
        } catch {
            // Ignore errors when reading directories
        }
        profileCombo.Items.AddRange(profiles.ToArray());

        // Select the current profile
        int selectedIndex = profiles.IndexOf(profile);
        if (selectedIndex >= 0) {
            profileCombo.SelectedIndex = selectedIndex;
        } else {
            profileCombo.SelectedIndex = 0; // Default to "Main save"
        }

        layout.Controls.Add(profileCombo);

        newGameButton = Button("New game...", DialogResult.None, Accent, Back);
        newGameButton.Click += (sender, e) => NewGame();
        layout.Controls.Add(newGameButton);

        layout.Controls.Add(new Label {
            Text = FooterText,
            ForeColor = Muted,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Margin = new Padding(0, 16, 0, 0),
        });

        if (sendReports != null) {
            // Opt-in: never ticked by default, and ticking it first shows what a report contains.
            autoReports = new CheckBox {
                Text = "Send problem reports to the developer automatically",
                Checked = sendReports == "always",
                AutoSize = true,
                Margin = new Padding(0, 16, 0, 0),
            };
            // On CheckedChanged, not Click: Narrator and other accessibility tools tick the box without a click.
            bool confirming = false;
            autoReports.CheckedChanged += (_, _) => {
                if (autoReports.Checked && !confirming) {
                    confirming = true;
                    if (!ReportDialogs.ConfirmAutomatic(this)) {
                        autoReports.Checked = false;
                    }
                    confirming = false;
                }
            };
            layout.Controls.Add(autoReports);
            var links = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(16, 0, 0, 0) };
            links.Controls.Add(Link("What is sent?", () => ReportDialogs.ShowWhatIsSent(this)));
            if (reportProblem != null) {
                links.Controls.Add(Link("Report a problem...", () => reportProblem(this)));
            }
            layout.Controls.Add(links);
        }

        var play = Button("Play", DialogResult.OK, Accent, Back);
        var quit = Button("Quit", DialogResult.Cancel, Color.FromArgb(52, 55, 64), Color.White);
        var buttons = new FlowLayoutPanel {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 14, 0, 0),
        };
        buttons.Controls.AddRange([play, quit]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = play;
        CancelButton = quit;

        (current == "Legacy" ? legacy : adapted).Checked = true;
    }

    private void NewGame() {
        // Create a new form for the name input
        using var dialog = new Form {
            Text = "New save",
            Icon = Icon,
            Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Back,
            ForeColor = Color.White,
        };
        var textBox = new TextBox { Width = 260, Margin = new Padding(0, 6, 0, 0) };
        var okButton = Button("OK", DialogResult.OK, Accent, Back);
        var cancelButton = Button("Cancel", DialogResult.Cancel, Color.FromArgb(52, 55, 64), Color.White);
        var buttons = new FlowLayoutPanel {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 14, 0, 0),
        };
        buttons.Controls.AddRange([okButton, cancelButton]);
        var layout = new TableLayoutPanel {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 14, 16, 12),
        };
        layout.Controls.Add(new Label { Text = "Name of the new save:", AutoSize = true });
        layout.Controls.Add(textBox);
        layout.Controls.Add(buttons);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        dialog.ActiveControl = textBox;

        // Owned by this window, so it always opens in front of it (without an owner it could open behind it).
        if (dialog.ShowDialog(this) == DialogResult.OK) {
            string name = textBox.Text;
            string? validationError = Settings.ValidateProfile(name);
            if (validationError != null) {
                MessageBox.Show(this, validationError, "Invalid name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check if the profile already exists
            string profileName = name.Trim();
            if (profileCombo.Items.Contains(profileName)) {
                // Select the existing profile
                profileCombo.SelectedItem = profileName;
            } else {
                // Add new profile to combo box and select it
                profileCombo.Items.Add(profileName);
                profileCombo.SelectedItem = profileName;
            }
        }
    }

    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        (legacy.Checked ? legacy : adapted).Focus(); // arrow keys switch, Enter plays
    }

    private static RadioButton AddOption(TableLayoutPanel layout, string title, string text) {
        var radio = new RadioButton {
            Text = title,
            Font = new Font("Segoe UI Semibold", 11f),
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 2),
        };
        var description = new Label {
            Text = text,
            ForeColor = Muted,
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(20, 0, 0, 0),
        };
        description.Click += (_, _) => radio.Checked = true; // the whole option is clickable
        layout.Controls.Add(radio);
        layout.Controls.Add(description);
        return radio;
    }

    private static LinkLabel Link(string text, Action click) {
        var link = new LinkLabel {
            Text = text,
            AutoSize = true,
            LinkColor = Color.FromArgb(150, 170, 255),
            ActiveLinkColor = Color.White,
            Margin = new Padding(0, 2, 14, 0),
        };
        link.LinkClicked += (_, _) => click();
        return link;
    }

    private static Button Button(string text, DialogResult result, Color back, Color fore) {
        var button = new Button {
            Text = text,
            DialogResult = result,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = new Font("Segoe UI Semibold", 10f),
            Size = new Size(100, 32),
            Margin = new Padding(8, 0, 0, 0),
        };
        button.FlatAppearance.BorderColor = back;
        return button;
    }
}
