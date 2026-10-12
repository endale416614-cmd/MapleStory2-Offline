using System.Diagnostics;

namespace MapleStory2Offline;

/// <summary>Where players report bugs and get help.</summary>
internal static class Community {
    public const string Discord = "https://discord.gg/bEEc9QsRx";

    public static void OpenDiscord() {
        try {
            Process.Start(new ProcessStartInfo(Discord) { UseShellExecute = true });
        } catch (Exception) {
            // no browser associated; the link is shown in the message
        }
    }

    /// <summary>A problem message that ends with the Discord link and offers to open it.</summary>
    public static void ShowProblem(string text, string title, MessageBoxIcon icon) {
        if (MessageBox.Show($"{text}\n\nReport bugs on our Discord: {Discord}\nOpen Discord now?", title,
                MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes) {
            OpenDiscord();
        }
    }
}
