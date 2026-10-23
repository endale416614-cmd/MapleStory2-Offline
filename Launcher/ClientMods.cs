using System.IO;

namespace MapleStory2Offline;

/// <summary>Client mod files that follow a setting: a file is served while it is on and kept as "<name>.off" while it is off.</summary>
public static class ClientMods {
    public static void SetFile(string modsDir, string mod, string relativePath, bool enabled) {
        string on = Path.Combine(modsDir, mod, relativePath), off = on + ".off";
        if (enabled && File.Exists(off) && !File.Exists(on)) {
            File.Move(off, on);
        } else if (!enabled && File.Exists(on)) {
            File.Move(on, off, true);
        }
    }
}