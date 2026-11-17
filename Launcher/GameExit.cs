using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MapleStory2Offline;

/// <summary>
/// LC-01 (BT-229, BT-231, BT-233): closing a game process the launcher started or found left over, and how the session report
/// names the game's exit. Kept apart from LauncherForm and SessionReport so the tests can link it.
/// </summary>
internal static class GameExit {
    /// <summary>A non-zero exit this soon after the player's Quit packet is read as a crash while closing.</summary>
    public static readonly TimeSpan ClosingWindow = TimeSpan.FromSeconds(30);

    // The game server's log line for a disconnect made by QuitHandler (Session.Disconnect logs the calling file); the time uses the PC's separator.
    private static readonly Regex QuitLine = new(@"^(?<h>\d\d)[:.](?<m>\d\d)[:.](?<s>\d\d)\.(?<ms>\d{3})\s.*QuitHandler\.cs", RegexOptions.Compiled);

    /// <summary>Ends the games and what they started. True when every one is gone; false when one refused (access denied).</summary>
    public static bool Close(IEnumerable<Process> games) {
        bool allGone = true;
        foreach (Process game in games) {
            try {
                if (!game.HasExited) {
                    ServerHost.EndTree(() => game.Kill(entireProcessTree: true), () => game.Kill());
                    game.WaitForExit(5000);
                }
                allGone &= game.HasExited;
            } catch (InvalidOperationException) {
                // already gone
            } catch (Win32Exception) {
                allGone = false;
            }
        }
        return allGone;
    }

    /// <summary>How long before <paramref name="now"/> the game server logged the player's last Quit, or null when it logged none.</summary>
    public static TimeSpan? QuitAge(string gameLog, DateTime now) {
        try {
            using var stream = new FileStream(gameLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            Match? last = null;
            while (reader.ReadLine() is { } line) {
                Match match = QuitLine.Match(line);
                if (match.Success) {
                    last = match;
                }
            }
            if (last == null) {
                return null;
            }
            var time = new TimeSpan(0, int.Parse(last.Groups["h"].Value), int.Parse(last.Groups["m"].Value), int.Parse(last.Groups["s"].Value),
                int.Parse(last.Groups["ms"].Value));
            TimeSpan age = now.TimeOfDay - time;
            return age < TimeSpan.Zero ? age + TimeSpan.FromDays(1) : age; // the log's clock passed midnight
        } catch (IOException) {
            return null;
        }
    }

    /// <summary>The report's problem line for a non-zero exit code of the game. <paramref name="sinceQuit"/> is the time since the player's Quit, if any.</summary>
    public static string Describe(int code, TimeSpan? sinceQuit) {
        string number = $"exit code {code} = 0x{code:X8}{Name(code)}";
        return sinceQuit is { } age && age <= ClosingWindow
            ? $"The game crashed while closing ({number}; {age.TotalSeconds:F0} s after your Quit)"
            : $"The game closed unexpectedly ({number})";
    }

    private static string Name(int code) => unchecked((uint) code) switch {
        0xC0000005 => ", access violation (crash)",
        0xC0000409 => ", stack buffer overrun (crash)",
        0xC00000FD => ", stack overflow (crash)",
        0xC0000374 => ", heap corruption (crash)",
        0xC0000017 or 0xE06D7363 => ", out of memory or unhandled exception (crash)",
        0x40010004 => ", closed by Windows / Task Manager",
        // BT-231: seen on healthy sessions (code 1) and after a Quit (0xCFFFFFFF); what they mean is not known.
        1 or 0xCFFFFFFF => ", a code whose meaning is not known",
        _ => "",
    };
}
