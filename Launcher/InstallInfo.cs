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
