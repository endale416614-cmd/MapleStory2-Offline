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
        "and party-size dungeons open to you (except Cathy Mart, which needs a full group).";
    public const string FooterText =
        "Both modes keep Meret pouches, the full Meret Market, mail that never expires and the chat commands. " +
        "Your characters work in either mode. To switch, close the game and start it again.";

    private static readonly Color Back = Color.FromArgb(28, 30, 36);
    private static readonly Color Muted = Color.FromArgb(190, 194, 204);
    private static readonly Color Accent = Color.FromArgb(255, 196, 92);

    private readonly RadioButton legacy;
    private readonly RadioButton adapted;

    /// <summary>"Legacy" or "Adapted": the selected option.</summary>
    public string Mode => legacy.Checked ? "Legacy" : "Adapted";

    public ModeChooser(string current, Icon icon) {
        Text = "Choose how to play";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
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
        layout.Controls.Add(new Label {
            Text = FooterText,
            ForeColor = Muted,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Margin = new Padding(0, 16, 0, 0),
        });

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
