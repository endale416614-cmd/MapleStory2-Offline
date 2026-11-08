using System.Security.Cryptography;

namespace MapleStory2Offline;

/// <summary>
/// Reads the client patch's files in the game's x64 folder (ClientLocator installs and removes them). No WinForms, so
/// Launcher.Tests links it.
/// </summary>
internal static class ClientPatch {
    /// <summary>sha256 of the game's own NxCharacter64.dll (Steam release, final NA version).</summary>
    public const string OriginalProxySha256 = "E742CA44B24AAD0787D73C5DA530EED16FA945BD539B1AB00ED0408AB69D0949";
    private const string Proxy = "NxCharacter64.dll";
    private const string Backup = "NxCharacter64.dll.bak";

    /// <summary>
    /// BT-216: what the player has to put back when the game's own NxCharacter64.dll is gone, or null. The patch's
    /// NxCharacter64.dll loads the game's own from NxCharacter64.dll.bak, so with the patch's file and no .bak (or with no
    /// NxCharacter64.dll at all, which installing would fill with the patch's) the game closes right after its window opens.
    /// Only a game folder counts, and it is only read: nothing may be changed in that state.
    /// </summary>
    public static string? MissingOriginal(string clientDir, string patchDir) {
        string proxy = Path.Combine(clientDir, Proxy);
        if (!File.Exists(Path.Combine(clientDir, "MapleStory2.exe")) || File.Exists(Path.Combine(clientDir, Backup))
            || (File.Exists(proxy) && Hash(proxy) != Hash(Path.Combine(patchDir, Proxy)) && !LoadsBackup(proxy))) {
            return null;
        }
        return $"The game's own NxCharacter64.dll is missing from the game folder:\n{clientDir}\n\n" +
               "The offline patch needs it, so the game would close right after its window opens. This usually happens when the " +
               "patch files are copied into the game folder by hand: the patch's NxCharacter64.dll then takes the place of the game's own.\n\n" +
               "Put the game's NxCharacter64.dll back: in Steam, right-click MapleStory 2, then Properties, Installed Files, Verify " +
               "integrity of game files. For a game you downloaded, copy NxCharacter64.dll from the download into the game's x64 " +
               "folder. Then start the launcher again: it installs the patch itself. Never copy the patch files by hand. If it " +
               "still fails, post the report on Discord.";
    }

    /// <summary>
    /// The session report's patch lines. Before the first game start the game's own NxCharacter64.dll and a missing
    /// AgarciumClient.dll are not installed yet (the launcher installs the patch then); "DIFFERENT" is a file that is neither
    /// the game's own nor the patch. The backup line names the state MissingOriginal stops at.
    /// </summary>
    public static IEnumerable<string> ReportLines(string clientDir, string patchDir, string originalSha256 = OriginalProxySha256) {
        bool backup = File.Exists(Path.Combine(clientDir, Backup));
        foreach (string dll in new[] { Proxy, "AgarciumClient.dll" }) {
            string installed = Path.Combine(clientDir, dll), shipped = Path.Combine(patchDir, dll);
            string hash = Hash(installed);
            string state = !File.Exists(installed) ? (dll == Proxy || backup ? "MISSING" : "not installed yet")
                : hash == originalSha256 ? "not installed yet (the game's own file)"
                : !File.Exists(shipped) ? "installed"
                : hash == Hash(shipped) ? "up to date" : "DIFFERENT from the shipped patch";
            yield return $"Patch {dll,-20}: {state}";
        }
        yield return "Original DLL backup : " + (backup ? "present"
            : MissingOriginal(clientDir, patchDir) == null ? "not yet (made on first start)"
            : File.Exists(Path.Combine(clientDir, Proxy)) ? "MISSING: the patch's NxCharacter64.dll took the place of the game's own (see TROUBLESHOOTING.txt [T9])"
            : "MISSING, and NxCharacter64.dll too (see TROUBLESHOOTING.txt [T9])");
    }

    /// <summary>A file's sha256, or "" when it is missing or cannot be read: the report is written whatever the files are.</summary>
    private static string Hash(string path) {
        try {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        } catch (Exception) {
            return "";
        }
    }

    /// <summary>
    /// Whether the file is a build of the patch's NxCharacter64.dll, this release's or an earlier one: every build loads
    /// NxCharacter64.dll.bak by that name, and the game's own file does not name it. False when it cannot be read.
    /// </summary>
    private static bool LoadsBackup(string path) {
        try {
            return File.ReadAllBytes(path).AsSpan().IndexOf("NxCharacter64.dll.bak"u8) >= 0;
        } catch (Exception) {
            return false;
        }
    }
}
