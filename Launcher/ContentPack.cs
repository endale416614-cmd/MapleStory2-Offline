using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MapleStory2Offline;

/// <summary>
/// Append-only content pack (PLAN_1.2.0_UNIFIED_CONTENT section 2). The pack folder holds pack.json and, per client
/// archive, &lt;stem&gt;.tail (the bytes appended after the untouched Global archive) and &lt;stem&gt;.m2h (the new header),
/// where stem is the archive file name without extension (Xml.tail, Xml.m2h for Data/Xml.m2d).
///
/// Apply, per archive: the file must be byte-identical to the Global archive (size and sha256 from pack.json); the
/// tail is appended, verified, then the .m2h is replaced and the original kept as &lt;stem&gt;.orig.m2h. An archive that
/// already ends with the tail is left alone. Anything else (unknown build, half-applied file, a Steam update) is
/// reported and nothing is changed. Restore truncates to the Global size and puts the .orig.m2h back, then verifies.
/// Every step is journaled in the pack folder like <see cref="UpdateInstaller"/> does.
/// </summary>
internal sealed class ContentPack {
    public const string FileName = "pack.json";
    private const string JournalName = "apply-journal.json";

    public sealed record Archive(string Path, long GlobalSize, string GlobalSha256, long TailSize, string TailSha256, string M2hSha256, string OrigM2hSha256) {
        public string Stem => System.IO.Path.GetFileNameWithoutExtension(Path);
        public string M2hPath => System.IO.Path.ChangeExtension(Path, ".m2h");
        public string OrigM2hPath => System.IO.Path.ChangeExtension(Path, ".orig.m2h");
    }

    public enum Status { NotApplied, Applied, Foreign }

    public sealed record ArchiveState(Archive Archive, Status Status, string Reason);

    public sealed class Journal {
        public string Version { get; set; } = "";
        public string State { get; set; } = "";   // applying, restoring, done
        public string Archive { get; set; } = "";
        public string Step { get; set; } = "";    // tail, m2h, truncate
        public string Utc { get; set; } = "";
    }

    public string Version { get; }
    public IReadOnlyList<Archive> Archives { get; }

    private ContentPack(string version, IReadOnlyList<Archive> archives) {
        Version = version;
        Archives = archives;
    }

    public static ContentPack Load(string packDir) => Parse(File.ReadAllBytes(Path.Combine(packDir, FileName)));

    // Strict, like ReleaseManifest: anything unexpected in a signed pack is a packaging error.
    public static ContentPack Parse(byte[] json) {
        try {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string version = root.GetProperty("version").GetString() ?? "";
            if (version.Length == 0) {
                throw new InvalidDataException($"{FileName} has no version");
            }
            var archives = new List<Archive>();
            foreach (JsonElement item in root.GetProperty("archives").EnumerateArray()) {
                string path = (item.GetProperty("path").GetString() ?? "").Replace('\\', '/');
                if (path.Length == 0 || Path.IsPathRooted(path) || path.Split('/').Any(part => part is "" or "." or "..")
                    || !path.EndsWith(".m2d", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException($"{FileName} lists an invalid archive path: {path}");
                }
                var archive = new Archive(path, item.GetProperty("globalSize").GetInt64(), Sha(item, "globalSha256"), item.GetProperty("tailSize").GetInt64(),
                    Sha(item, "tailSha256"), Sha(item, "m2hSha256"), Sha(item, "origM2hSha256"));
                if (archive.GlobalSize <= 0 || archive.TailSize <= 0) {
                    throw new InvalidDataException($"{FileName} lists {path} without valid sizes");
                }
                if (archives.Any(other => string.Equals(other.Path, path, StringComparison.OrdinalIgnoreCase) || other.Stem.Equals(archive.Stem, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidDataException($"{FileName} lists {path} twice");
                }
                archives.Add(archive);
            }
            if (archives.Count == 0) {
                throw new InvalidDataException($"{FileName} lists no archives");
            }
            return new ContentPack(version, archives);
        } catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) {
            throw new InvalidDataException($"{FileName} is not readable: {ex.Message}");
        }
    }

    private static string Sha(JsonElement item, string name) {
        string sha = (item.GetProperty(name).GetString() ?? "").ToLowerInvariant();
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) {
            throw new InvalidDataException($"{FileName} has an invalid {name}");
        }
        return sha;
    }

    /// <summary>State of every archive in the client. Foreign carries the reason in words.</summary>
    public IEnumerable<ArchiveState> State(string clientRoot) => Archives.Select(archive => State(clientRoot, archive));

    public static ArchiveState State(string clientRoot, Archive archive) {
        string path = Path.Combine(clientRoot, archive.Path), m2h = Path.Combine(clientRoot, archive.M2hPath);
        if (!File.Exists(path) || !File.Exists(m2h)) {
            return new ArchiveState(archive, Status.Foreign, $"{archive.Path} or its .m2h is missing");
        }
        long size = new FileInfo(path).Length;
        string m2hHash = Hash(m2h);
        if (size == archive.GlobalSize) {
            if (Hash(path) != archive.GlobalSha256) {
                return new ArchiveState(archive, Status.Foreign, $"{archive.Path} has the Global size but not the Global sha256 (unknown client build)");
            }
            return m2hHash == archive.OrigM2hSha256
                ? new ArchiveState(archive, Status.NotApplied, "")
                : new ArchiveState(archive, Status.Foreign, $"{archive.M2hPath} is not the Global header");
        }
        if (size == archive.GlobalSize + archive.TailSize) {
            if (Hash(path, archive.GlobalSize, archive.TailSize) != archive.TailSha256) {
                return new ArchiveState(archive, Status.Foreign, $"{archive.Path} has the applied size but its tail does not match the pack");
            }
            // The original header is allowed: an apply or restore interrupted between the tail and the header step.
            return m2hHash == archive.M2hSha256 || m2hHash == archive.OrigM2hSha256
                ? new ArchiveState(archive, Status.Applied, "")
                : new ArchiveState(archive, Status.Foreign, $"{archive.M2hPath} is neither the Global nor the pack header");
        }
        return new ArchiveState(archive, Status.Foreign, $"{archive.Path} has an unexpected size ({size:N0} bytes; expected {archive.GlobalSize:N0} or {archive.GlobalSize + archive.TailSize:N0})");
    }

    /// <summary>
    /// Appends every tail and installs every new header; idempotent. Throws <see cref="InvalidOperationException"/>
    /// with the reason and changes nothing when any archive is Foreign or any pack file does not match pack.json.
    /// Returns false when Windows refused write access (the caller retries with <see cref="ApplyElevated"/>).
    /// </summary>
    public static bool Apply(string clientRoot, string packDir) {
        ContentPack pack = Load(packDir);
        List<ArchiveState> states = pack.State(clientRoot).ToList();
        Refuse(states, "applied");
        foreach (Archive archive in pack.Archives) {
            string tail = Path.Combine(packDir, archive.Stem + ".tail"), m2h = Path.Combine(packDir, archive.Stem + ".m2h");
            if (!File.Exists(tail) || new FileInfo(tail).Length != archive.TailSize || Hash(tail) != archive.TailSha256) {
                throw new InvalidOperationException($"The content pack file {archive.Stem}.tail is missing or does not match pack.json. Nothing was changed.");
            }
            if (!File.Exists(m2h) || Hash(m2h) != archive.M2hSha256) {
                throw new InvalidOperationException($"The content pack file {archive.Stem}.m2h is missing or does not match pack.json. Nothing was changed.");
            }
        }
        var journal = new Journal { Version = pack.Version, State = "applying", Utc = DateTime.UtcNow.ToString("O") };
        try {
            foreach (ArchiveState state in states) {
                Archive archive = state.Archive;
                string target = Path.Combine(clientRoot, archive.Path);
                if (state.Status == Status.NotApplied) {
                    Step(packDir, journal, archive.Path, "tail");
                    try {
                        using (FileStream append = new(target, FileMode.Append, FileAccess.Write, FileShare.None))
                        using (FileStream source = File.OpenRead(Path.Combine(packDir, archive.Stem + ".tail"))) {
                            source.CopyTo(append);
                        }
                        if (new FileInfo(target).Length != archive.GlobalSize + archive.TailSize || Hash(target, archive.GlobalSize, archive.TailSize) != archive.TailSha256) {
                            throw new InvalidOperationException($"{archive.Path} did not verify after the tail was appended.");
                        }
                    } catch (Exception) {
                        Truncate(target, archive.GlobalSize); // back to the untouched Global archive
                        throw;
                    }
                }
                string header = Path.Combine(clientRoot, archive.M2hPath), orig = Path.Combine(clientRoot, archive.OrigM2hPath);
                if (Hash(header) != archive.M2hSha256) {
                    Step(packDir, journal, archive.Path, "m2h");
                    if (!File.Exists(orig) || Hash(orig) != archive.OrigM2hSha256) {
                        CopyOver(header, orig);
                    }
                    CopyOver(Path.Combine(packDir, archive.Stem + ".m2h"), header);
                }
            }
            journal.State = "done";
            WriteJournal(packDir, journal);
            return true;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }

    /// <summary>
    /// Truncates every applied archive back to the Global size and puts the original header back, verifying the Global
    /// sha256 afterwards. Refuses and changes nothing when any archive is Foreign. Returns false when Windows refused
    /// write access.
    /// </summary>
    public static bool Restore(string clientRoot, string packDir) {
        ContentPack pack = Load(packDir);
        List<ArchiveState> states = pack.State(clientRoot).ToList();
        Refuse(states, "removed");
        var journal = new Journal { Version = pack.Version, State = "restoring", Utc = DateTime.UtcNow.ToString("O") };
        try {
            foreach (ArchiveState state in states.Where(state => state.Status == Status.Applied)) {
                Archive archive = state.Archive;
                string target = Path.Combine(clientRoot, archive.Path);
                string header = Path.Combine(clientRoot, archive.M2hPath), orig = Path.Combine(clientRoot, archive.OrigM2hPath);
                if (Hash(header) != archive.OrigM2hSha256) {
                    if (!File.Exists(orig) || Hash(orig) != archive.OrigM2hSha256) {
                        throw new InvalidOperationException($"{archive.OrigM2hPath} is missing or damaged, so the Global header cannot be put back. Nothing was changed.");
                    }
                    Step(packDir, journal, archive.Path, "m2h");
                    CopyOver(orig, header);
                }
                Step(packDir, journal, archive.Path, "truncate");
                Truncate(target, archive.GlobalSize);
                if (Hash(target) != archive.GlobalSha256) {
                    throw new InvalidOperationException($"{archive.Path} does not match the Global archive after the tail was removed. Verify the game files in Steam.");
                }
                if (File.Exists(orig)) {
                    File.Delete(orig);
                }
            }
            journal.State = "done";
            WriteJournal(packDir, journal);
            return true;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }

    /// <summary>Runs "--apply-content-pack" as administrator, the way <see cref="ClientLocator.InstallPatchElevated"/> does.</summary>
    public static bool ApplyElevated(string clientRoot, string packDir, string mode = "--apply-content-pack") {
        try {
            using Process? process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
                UseShellExecute = true,
                Verb = "runas",
                ArgumentList = { mode, clientRoot, packDir },
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        } catch (System.ComponentModel.Win32Exception) {
            return false; // the player said no to the Windows prompt
        }
    }

    private static void Refuse(List<ArchiveState> states, string verb) {
        if (states.Where(state => state.Status == Status.Foreign).ToList() is { Count: > 0 } foreign) {
            throw new InvalidOperationException($"The content pack cannot be {verb}: {string.Join("; ", foreign.Select(state => state.Reason))}. " +
                "Nothing was changed. Verify the game files in Steam and try again.");
        }
    }

    private static void Truncate(string path, long length) {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
    }

    private static void Step(string packDir, Journal journal, string archive, string step) {
        journal.Archive = archive;
        journal.Step = step;
        WriteJournal(packDir, journal);
    }

    private static void WriteJournal(string packDir, Journal journal) {
        string path = Path.Combine(packDir, JournalName), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }

    /// <summary>Support text for reports: pack version, each archive by size and header (no full hash), and the last journal.</summary>
    public static string Describe(string clientRoot, string packDir) {
        if (!File.Exists(Path.Combine(packDir, FileName))) {
            return "Content pack  : none (no ContentPack folder)";
        }
        ContentPack pack;
        try {
            pack = Load(packDir);
        } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            return $"Content pack  : DAMAGED ({ex.Message})";
        }
        var lines = new List<string> { $"Content pack  : {pack.Version}" };
        foreach (Archive archive in pack.Archives) {
            string path = Path.Combine(clientRoot, archive.Path), m2h = Path.Combine(clientRoot, archive.M2hPath);
            string state;
            try {
                if (!File.Exists(path) || !File.Exists(m2h)) {
                    state = "MISSING";
                } else {
                    long size = new FileInfo(path).Length;
                    string header = Hash(m2h);
                    state = (size == archive.GlobalSize ? "Global size" : size == archive.GlobalSize + archive.TailSize ? "applied size" : $"UNEXPECTED size {size:N0}")
                            + ", " + (header == archive.M2hSha256 ? "pack header" : header == archive.OrigM2hSha256 ? "Global header" : "UNKNOWN header");
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                state = "unreadable: " + ex.Message;
            }
            lines.Add($"  {archive.Path}: {state}");
        }
        string journal = Path.Combine(packDir, JournalName);
        string last = "none";
        if (File.Exists(journal)) {
            try {
                Journal? j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journal));
                last = j == null ? "empty" : $"{j.State} {j.Version} {j.Archive} {j.Step} at {j.Utc}";
            } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                last = "unreadable: " + ex.Message;
            }
        }
        lines.Add($"Last journal  : {last}");
        return string.Join(Environment.NewLine, lines);
    }

    // A header is never half-written: copy next to it, then rename over it (one NTFS operation).
    private static void CopyOver(string source, string target) {
        string temp = target + ".tmp";
        File.Copy(source, temp, true);
        File.Move(temp, target, true);
    }

    /// <summary>
    /// Undoes a tail append that stopped halfway (the journal says "applying", step "tail"): when that archive is longer
    /// than the Global archive but not a fully applied one, and its first GlobalSize bytes still are the Global archive,
    /// truncates it back to the Global size (the next Apply appends again). Returns what was repaired, or null.
    /// </summary>
    public static string? RecoverInterrupted(string clientRoot, string packDir) {
        string journalPath = Path.Combine(packDir, JournalName);
        if (!File.Exists(journalPath)) {
            return null;
        }
        Journal? journal;
        try {
            journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath));
        } catch (JsonException) {
            return null; // an unreadable journal names no archive
        }
        if (journal is not { State: "applying", Step: "tail" }) {
            return null;
        }
        ContentPack pack = Load(packDir);
        Archive? archive = pack.Archives.FirstOrDefault(a => a.Path == journal.Archive);
        string? target = archive == null ? null : Path.Combine(clientRoot, archive.Path);
        if (archive == null || journal.Version != pack.Version || !File.Exists(target)) {
            return null;
        }
        long size = new FileInfo(target).Length, applied = archive.GlobalSize + archive.TailSize;
        bool interrupted = (size > archive.GlobalSize && size < applied)
                            || (size == applied && Hash(target, archive.GlobalSize, archive.TailSize) != archive.TailSha256);
        if (!interrupted || Hash(target, 0, archive.GlobalSize) != archive.GlobalSha256) {
            return null;
        }
        Truncate(target, archive.GlobalSize);
        journal.State = "recovered";
        WriteJournal(packDir, journal);
        return $"The last change to the unified content was interrupted; the unfinished part of {archive.Path} was removed.";
    }

    private static string Hash(string path) {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>sha256 of <paramref name="length"/> bytes starting at <paramref name="offset"/>.</summary>
    private static string Hash(string path, long offset, long length) {
        using FileStream stream = File.OpenRead(path);
        stream.Position = offset;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1 << 20];
        for (long left = length; left > 0;) {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0) {
                break;
            }
            hash.AppendData(buffer, 0, read);
            left -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Gets the content pack version applied to all archives. Returns null when no pack is applied,
    /// or throws InvalidOperationException if there's a mix of applied and not-applied archives.
    /// </summary>
    public string? AppliedVersion(string clientRoot) {
        var states = State(clientRoot).ToList();

        // Check if any archive is in Foreign state
        if (states.Any(s => s.Status == Status.Foreign)) {
            var foreign = states.Where(s => s.Status == Status.Foreign);
            throw new InvalidOperationException($"The game files do not match content pack {Version}: {string.Join("; ", foreign.Select(s => s.Reason))}. Verify the game files in Steam and try again.");
        }

        // If all archives are NotApplied, return null
        if (states.All(s => s.Status == Status.NotApplied)) {
            return null;
        }

        // If all archives are Applied, return the pack version
        if (states.All(s => s.Status == Status.Applied)) {
            return Version;
        }

        // Mixed states - throw exception with specific error message
        var notApplied = states.Where(s => s.Status == Status.NotApplied);
        throw new InvalidOperationException($"The content pack is only partly applied: {string.Join(", ", notApplied.Select(s => s.Archive.Path))}. Press Play again to finish applying it, or remove the unified content.");
    }

    /// <summary>
    /// Reads the contentPack field from data-build.json. Returns null when the file or the contentPack field is absent
    /// or JSON null; throws InvalidDataException for invalid JSON, a root that is not an object, or a contentPack that is
    /// empty or not a string.
    /// </summary>
    public static string? ReadDataBuildPack(string dataBuildJson) {
        if (!File.Exists(dataBuildJson)) return null;

        try {
            string json = File.ReadAllText(dataBuildJson);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) {
                throw new InvalidDataException($"{dataBuildJson}: the root is not a JSON object");
            }

            if (root.TryGetProperty("contentPack", out JsonElement contentPackElement)) {
                switch (contentPackElement.ValueKind) {
                    case JsonValueKind.Null:
                        return null;
                    case JsonValueKind.String:
                        string contentPack = contentPackElement.GetString() ?? "";
                        if (string.IsNullOrWhiteSpace(contentPack)) {
                            throw new InvalidDataException($"{dataBuildJson}: contentPack is empty");
                        }
                        return contentPack;
                    default:
                        throw new InvalidDataException($"{dataBuildJson}: contentPack is not a string");
                }
            }

            // no contentPack field: Global data
            return null;
        } catch (JsonException ex) {
            throw new InvalidDataException($"{dataBuildJson} is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generates an error message when there's a mismatch between applied content pack and server data.
    /// Returns null if there's no error (both are null or both equal).
    /// </summary>
    public static string? DataLockError(string? appliedPack, string? dataPack) {
        // No error case: both null or both equal
        if ((appliedPack == null && dataPack == null) ||
            (appliedPack != null && dataPack != null && string.Equals(appliedPack, dataPack, StringComparison.Ordinal))) {
            return null;
        }

        // Generate appropriate error message based on mismatch type
        if (appliedPack != null && dataPack == null) {
            return $"The unified content (pack {appliedPack}) is applied to the game, but the server data is the Global data. Update the launcher, or turn the unified content off in launcher.ini (UnifiedContent=false).";
        }
        else if (appliedPack == null && dataPack != null) {
            return $"The server data was built for the unified content (pack {dataPack}), but the game has no content pack applied. Turn the unified content on (UnifiedContent=true in launcher.ini) and press Play, or repair the installation.";
        }
        else {
            // Both non-null but different
            return $"The game has content pack {appliedPack}, but the server data was built for pack {dataPack}. Update the launcher, or turn the unified content off in launcher.ini (UnifiedContent=false).";
        }
    }
}
