using System.Security.Cryptography;

namespace MapleStory2Offline;

/// <summary>
/// Before the servers start: brings the client archives to the state UnifiedContent asks for, picks the server data
/// folder that matches the client (the pack's GameData only when the pack is fully applied), and runs the data lock.
/// </summary>
internal static class ContentPackFlow {
    public sealed record Result(string DataDir, string? AppliedVersion, string? Warning, string? Error);

    public static Result Prepare(bool unifiedContent, string clientRoot, string packDir, string globalDataDir,
        Func<string, string, string, bool> elevated) {
        string? warning = null;
        try {
            if (!File.Exists(Path.Combine(packDir, ContentPack.FileName))) {
                return Lock(globalDataDir, null, null);
            }
            ContentPack pack = ContentPack.Load(packDir);
            // Recover any interrupted applies before proceeding
            warning = ContentPack.RecoverInterrupted(clientRoot, packDir);
            // Unified off and no archive longer than the Global one (no pack, this or an earlier one): nothing to remove, so
            // Play skips hashing the Global archives (several GB).
            if (!unifiedContent && pack.Archives.All(a => !File.Exists(Path.Combine(clientRoot, a.Path))
                    || new FileInfo(Path.Combine(clientRoot, a.Path)).Length <= a.GlobalSize)) {
                return Lock(globalDataDir, null, warning);
            }
            List<ContentPack.ArchiveState> states = pack.State(clientRoot).ToList();
            if (states.Any(s => s.Status == ContentPack.Status.Foreign)) {
                // A client the pack does not fit (Steam update, other build) plays Global and is never written.
                if (unifiedContent || states.Any(s => s.Status == ContentPack.Status.Applied)) {
                    string reasons = string.Join("; ", states.Where(s => s.Status == ContentPack.Status.Foreign).Select(s => s.Reason));
                    warning = $"The unified content does not fit this game client ({reasons}). The game starts without it; verify the game files in Steam to use it again.";
                }
                return Lock(globalDataDir, null, warning);
            }
            bool changed = false;
            if (unifiedContent && !states.All(s => FullyApplied(clientRoot, s))) {
                changed = true;
                try {
                    if (!ContentPack.Apply(clientRoot, packDir) && !elevated(clientRoot, packDir, "--apply-content-pack")) {
                        warning = "The unified content could not be written to the game folder. The game starts without it.";
                    }
                } catch (InvalidOperationException ex) {
                    warning = ex.Message;
                }
            } else if (!unifiedContent && states.Any(s => s.Status is ContentPack.Status.Applied or ContentPack.Status.EarlierPack)) {
                changed = true;
                try {
                    if (!ContentPack.Restore(clientRoot, packDir) && !elevated(clientRoot, packDir, "--restore-content-pack")) {
                        return new Result(globalDataDir, null, null, "The unified content could not be removed from the game folder. Run the launcher as administrator once, or set UnifiedContent=true.");
                    }
                } catch (InvalidOperationException ex) {
                    warning = ex.Message;
                }
            }
            if (changed) {
                states = pack.State(clientRoot).ToList();
            }
            if (states.All(s => FullyApplied(clientRoot, s))) {
                return Lock(Path.Combine(packDir, "GameData"), pack.Version, warning);
            }
            if (states.All(s => s.Status == ContentPack.Status.NotApplied)) {
                return Lock(globalDataDir, null, warning);
            }
            return new Result(globalDataDir, null, warning, "The unified content is only partly applied. Press Play again; if this repeats, verify the game files in Steam.");
        } catch (InvalidDataException ex) {
            return new Result(globalDataDir, null, warning, $"The content pack is damaged: {ex.Message}. Repair the installation, or delete the ContentPack folder to play the Global game.");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new Result(globalDataDir, null, warning, $"The unified content could not be prepared: {ex.Message}. Close Steam and any virus scan of the game folder, then press Play again.");
        }
    }

    // Applied needs the pack header too: tail + Global header is an interrupted apply (Apply repairs it).
    private static bool FullyApplied(string clientRoot, ContentPack.ArchiveState state) =>
        state.Status == ContentPack.Status.Applied
        && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(clientRoot, state.Archive.M2hPath)))).ToLowerInvariant() == state.Archive.M2hSha256;

    private static Result Lock(string dataDir, string? appliedPack, string? warning) {
        string? dataPack;
        try {
            dataPack = ContentPack.ReadDataBuildPack(Path.Combine(dataDir, "data-build.json"));
        } catch (InvalidDataException ex) {
            return new Result(dataDir, appliedPack, warning, ex.Message);
        }
        // Check if the server data of the unified content is missing
        if (appliedPack != null && !File.Exists(Path.Combine(dataDir, "maple-data.db"))) {
            return new Result(dataDir, appliedPack, warning, "The server data of the unified content is missing (ContentPack\\GameData\\maple-data.db). Repair the installation, or set UnifiedContent=false.");
        }
        return new Result(dataDir, appliedPack, warning, ContentPack.DataLockError(appliedPack, dataPack));
    }
}