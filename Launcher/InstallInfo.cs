using System.Text.Json;

namespace MapleStory2Offline;

/// <summary>
/// install.json in the program folder marks an installed copy (written by Setup):
/// { "installId": "...", "dataDir": "%LOCALAPPDATA%\\MapleStory 2 Offline" }.
/// Without it the copy is portable: player data lives in the program folder and the id is kept in "install-id".
/// </summary>
internal static class InstallInfo {
    public const string FileName = "install.json";
    public const string DefaultDataDir = @"%LOCALAPPDATA%\MapleStory 2 Offline";

    public sealed record Data(string InstallId, string DataDir);

    public static (string DataRoot, bool Installed, string InstallId) Read(string root) {
        string path = Path.Combine(root, FileName);
        if (File.Exists(path)) {
            try {
                Data? data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data is { DataDir.Length: > 0 }) {
                    return (Path.GetFullPath(Environment.ExpandEnvironmentVariables(data.DataDir)), true, data.InstallId);
                }
            } catch (Exception) {
                // a damaged install.json: fall back to a portable layout rather than refusing to start
            }
        }
        return (root, false, PortableId(root));
    }

    public static void Write(string root, string installId, string dataDir) {
        File.WriteAllText(Path.Combine(root, FileName), JsonSerializer.Serialize(new Data(installId, dataDir), new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Copies the files the web server of an older version uploaded into its program folder to a new program folder, so Setup's
    /// swap does not delete them before the new web server's first start copied them into the save (BT-15). The shipped system
    /// folder stays the new one's, and so does a file already there. Throws when a file cannot be copied.
    /// </summary>
    public static void CarryWebData(string oldProgramDir, string newProgramDir) {
        string webData = Path.Combine("Maple2", "Maple2.Server.Web", "Data"), from = Path.Combine(oldProgramDir, webData);
        if (!Directory.Exists(from)) {
            return;
        }
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) {
            string relative = Path.GetRelativePath(from, file), target = Path.Combine(newProgramDir, webData, relative);
            if (!relative.StartsWith("system" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !File.Exists(target)) {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }

    private static string PortableId(string root) {
        string path = Path.Combine(root, "install-id");
        try {
            if (File.Exists(path)) {
                return File.ReadAllText(path).Trim();
            }
            string id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        } catch (Exception) {
            return "unknown";
        }
    }
}
