using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MapleStory2Offline;

/// <summary>
/// Problem reports for the developer (docs/engineering/DIAGNOSTIC_REPORTS.md), sent only with the player's consent
/// (ReportDialogs): the session report without what the game client wrote and without personal details, as gzip JSON,
/// queued in the data folder and sent when the launcher is online. No WinForms here, so Launcher.Tests links this file.
/// </summary>
internal static class ProblemReport {
    /// <summary>The report service, set at release time. Empty until the service is deployed: reports then stay queued.</summary>
    public const string ServiceUrl = "https://ms2-reports.ms2-reports.workers.dev";
    public const int MaxReportChars = 400_000;
    public const int MaxNoteChars = 2_000;
    public const int QueueLimit = 20;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private const string CutMarker = "\n... (report cut: too long)";
    // What the game client wrote into the server logs (LogSendHandler: its messages, and socket errors with a dump of
    // the last packet, which can hold names and chat) and save-conflict values (free text such as a motto). A dropped
    // line takes its continuation lines with it; any run of 8 hex bytes is a packet dump. The report shortens long
    // lines, so a client error is matched without its closing bracket.
    private static readonly string[] ClientMarkers = ["Client Log:", "[SendOp:", "[LastPacket:", "proposed='"];
    private static readonly Regex ClientError = new(@"\[[^\]\n]*exception");
    private static readonly Regex HexRun = new(@"(?:[0-9A-Fa-f]{2} ){8,}");
    // SessionReport writes these sections after the client log; a client line can look like any other header.
    private static readonly string[] AfterClientLog = ["-- Offline guard log", "-- end of report --"];
    private static readonly Regex SectionHeader = new(@"^-- .+ -{3,}\r?$|^-- end of report --\r?$");
    private static readonly Regex NewEntry = new(@"^(?:\d\d[:.]\d\d[:.]\d\d(?:\.\d{3})?\s|\* )");

    /// <summary>Tests point this at a local server (MS2OFFLINE_REPORT_URL); players use ServiceUrl. Null: no service.</summary>
    public static string? Url() => Environment.GetEnvironmentVariable("MS2OFFLINE_REPORT_URL") is { Length: > 0 } url ? url
        : ServiceUrl.StartsWith("https://", StringComparison.Ordinal) ? ServiceUrl : null;

    /// <summary>
    /// The text that may leave the PC, made from the session report before its redaction (SessionReport.RawText): the
    /// game client's text goes first, while its markers are intact (a user named "Game" would change them), then Redact,
    /// then the names, then the cut.
    /// </summary>
    public static string ForUpload(string rawReport, IEnumerable<string> names) {
        string text = ReplaceNames(Redact(DropClientText(rawReport)), names);
        return text.Length <= MaxReportChars ? text : text[..MaxReportChars] + CutMarker;
    }

    /// <summary>The player's own note as sent: redacted, names replaced, at most MaxNoteChars.</summary>
    public static string Note(string note, IEnumerable<string> names) {
        string text = ReplaceNames(Redact(note), names);
        return text.Length <= MaxNoteChars ? text : text[..MaxNoteChars];
    }

    /// <summary>Without the game client's log section and without what the client wrote into the server logs.</summary>
    public static string DropClientText(string report) {
        var kept = new List<string>();
        bool clientSection = false, continuation = false;
        foreach (string line in report.Split('\n')) {
            if (clientSection && !AfterClientLog.Any(header => line.StartsWith(header, StringComparison.Ordinal))) {
                continue;
            }
            if (SectionHeader.IsMatch(line)) {
                clientSection = line.StartsWith("-- Game client log", StringComparison.Ordinal);
                // A log tail can begin inside a dropped message (a short dump whose [LastPacket: line fell outside it).
                continuation = line.StartsWith("-- Server log", StringComparison.Ordinal) || line.StartsWith("-- Trigger states", StringComparison.Ordinal);
                if (!clientSection) {
                    kept.Add(line);
                }
                continue;
            }
            if (NewEntry.IsMatch(line)) {
                continuation = false;
            }
            if (ClientMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)) || ClientError.IsMatch(line)) {
                continuation = true;
                continue;
            }
            if (!continuation && !HexRun.IsMatch(line)) {
                kept.Add(line);
            }
        }
        return string.Join('\n', kept);
    }

    /// <summary>Removes the user profile folder, the Windows user name and PC name, and machine ids.</summary>
    public static string Redact(string text) {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        foreach (string secret in new[] { Environment.UserName, Environment.MachineName }) {
            if (secret.Length >= 3) {
                text = Regex.Replace(text, $@"\b{Regex.Escape(secret)}\b", "<redacted>", RegexOptions.IgnoreCase);
            }
        }
        text = Regex.Replace(text, @"Machine: [0-9a-fA-F-]{8,}", "Machine: <redacted>");
        // Login server: Logging in to game as { ..., "machineId": "<client hardware id>", ... }; a save conflict: MachineId: proposed='<id>'
        return Regex.Replace(text, @"(machine[ _-]?id[""']?\s*[:=]\s*(?:proposed=)?[""']?)[0-9a-fA-F][0-9a-fA-F-]{7,}", "$1<redacted>",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// The names kept out of uploads: the character and account names in every save profile, the profile folder names,
    /// the Windows user name, PC name and user folder name. Unreadable names a save (or the save folder) that could not
    /// be read: then nothing may be sent, because that save's character names would stay in the server logs.
    /// </summary>
    public static (List<string> Names, string? Unreadable) Names(string dataRoot) {
        var names = new List<string> {
            Environment.UserName, Environment.MachineName, Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };
        string saveData = Settings.SaveDirFor(dataRoot, "");
        if (!Directory.Exists(saveData)) {
            return (names, null);
        }
        string? unreadable = null;
        try {
            names.AddRange(Directory.GetDirectories(saveData).Select(Path.GetFileName).OfType<string>()
                .Where(folder => !folder.Equals("backups", StringComparison.OrdinalIgnoreCase)));
            foreach (string save in Directory.GetFiles(saveData, "game-server.db", SearchOption.AllDirectories)) {
                if (!ReadNames(save, names)) {
                    unreadable ??= save;
                }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            unreadable ??= saveData;
        }
        return (names, unreadable);
    }

    /// <summary>The player's name for what Names could not read: a profile folder, "main save" or "save folder".</summary>
    public static string SaveLabel(string dataRoot, string path) {
        string full = Path.GetFullPath(path), saveData = Path.GetFullPath(Settings.SaveDirFor(dataRoot, ""));
        if (string.Equals(full, saveData, StringComparison.OrdinalIgnoreCase)) {
            return "save folder";
        }
        string? folder = Path.GetDirectoryName(full);
        return string.Equals(folder, saveData, StringComparison.OrdinalIgnoreCase) ? "main save" : Path.GetFileName(folder) ?? "save";
    }

    /// <summary>The report as sent: gzip JSON of schema 1 (design doc section 1), from the unredacted session report.</summary>
    public static byte[] Build(Guid id, DateTime createdUtc, string kind, string installId, string platform, string launcher,
        string rawReport, string? note, IEnumerable<string> names) {
        List<string> list = names.ToList();
        var payload = new {
            schema = 1,
            reportId = id.ToString("D"),
            installId,
            createdUtc = createdUtc.ToString("o"),
            kind,
            platform,
            launcher,
            note = note == null ? null : Note(note, list),
            report = ForUpload(rawReport, list),
        };
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) {
            JsonSerializer.Serialize(gzip, payload);
        }
        return buffer.ToArray();
    }

    /// <summary>Queues a report as &lt;id&gt;.&lt;kind&gt;.json.gz (written through a .part file); returns its path.</summary>
    public static string Enqueue(string queueDir, Guid id, string kind, byte[] payload, DateTime nowUtc) {
        Directory.CreateDirectory(queueDir);
        string path = Path.Combine(queueDir, $"{id:D}.{kind}.json.gz"), part = path + ".part";
        File.WriteAllBytes(part, payload);
        File.Move(part, path, true);
        File.SetLastWriteTimeUtc(path, nowUtc);
        Pending(queueDir, nowUtc);
        return path;
    }

    /// <summary>Deletes the queued reports of these kinds (the player chose never to send reports after problems).</summary>
    public static void Withdraw(string queueDir, params string[] kinds) {
        if (!Directory.Exists(queueDir)) {
            return;
        }
        foreach (string kind in kinds) {
            foreach (FileInfo file in new DirectoryInfo(queueDir).GetFiles($"*.{kind}.json.gz")) {
                TryDelete(file);
            }
        }
    }

    /// <summary>
    /// The queued reports, oldest first. Drops reports older than MaxAge, all but the newest QueueLimit, and .part files
    /// left by an interrupted write (an hour old: a newer one may still be written by another launcher process).
    /// </summary>
    public static List<string> Pending(string queueDir, DateTime nowUtc) {
        if (!Directory.Exists(queueDir)) {
            return [];
        }
        var folder = new DirectoryInfo(queueDir);
        foreach (FileInfo part in folder.GetFiles("*.part").Where(file => file.LastWriteTimeUtc < nowUtc - TimeSpan.FromHours(1))) {
            TryDelete(part);
        }
        List<FileInfo> reports = folder.GetFiles("*.json.gz")
            .OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToList();
        List<FileInfo> dropped = reports.Where(file => file.LastWriteTimeUtc < nowUtc - MaxAge).ToList();
        dropped.AddRange(reports.Except(dropped).SkipLast(QueueLimit));
        dropped.ForEach(TryDelete);
        return reports.Except(dropped).Select(file => file.FullName).ToList();
    }

    /// <summary>
    /// Sends the queued reports, oldest first, each POSTed to &lt;url&gt;/v1/report within 10 s. Sent (2xx) and rejected
    /// (400, 413) reports are deleted; any other answer, a timeout or no network stops until the next time; a file that
    /// cannot be read waits for the next time. Returns the number sent and adds their paths to <paramref name="sent"/>;
    /// only a cancelled <paramref name="token"/> throws.
    /// </summary>
    public static async Task<int> SendAsync(string queueDir, string? url, HttpMessageHandler handler, DateTime nowUtc,
        CancellationToken token, ISet<string>? sent = null) {
        if (string.IsNullOrEmpty(url)) {
            return 0;
        }
        List<string> pending;
        try {
            pending = Pending(queueDir, nowUtc);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return 0;
        }
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleStory2-Offline-Launcher");
        int count = 0;
        foreach (string path in pending) {
            token.ThrowIfCancellationRequested();
            byte[] body;
            try {
                body = await File.ReadAllBytesAsync(path, token);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                continue; // in use, gone or no access: the next time
            }
            HttpStatusCode status;
            try {
                using var content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.ContentEncoding.Add("gzip");
                using HttpResponseMessage response = await http.PostAsync(url.TrimEnd('/') + "/v1/report", content, token);
                status = response.StatusCode;
            } catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !token.IsCancellationRequested)) {
                break; // no network or a timeout
            }
            if ((int) status is >= 200 and < 300) {
                TryDelete(new FileInfo(path));
                sent?.Add(path);
                count++;
            } else if (status is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge) {
                TryDelete(new FileInfo(path)); // the service will never take this one
            } else {
                break;
            }
        }
        return count;
    }

    private static bool ReadNames(string save, List<string> names) {
        try {
            using var connection = new SqliteConnection($"Data Source={save};Mode=ReadOnly;Pooling=False");
            connection.Open();
            foreach (string sql in new[] { "SELECT Name FROM \"character\"", "SELECT Username FROM account" }) {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                try {
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read()) {
                        if (!reader.IsDBNull(0)) {
                            names.Add(reader.GetString(0));
                        }
                    }
                } catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)) {
                    // a new save without characters yet
                }
            }
            return true;
        } catch (Exception) {
            return false; // damaged, locked or unreadable
        }
    }

    // Whole words only (Bob, not Bobby), any case; names of 1 character and the default account "offline" stay.
    private static string ReplaceNames(string text, IEnumerable<string> names) {
        foreach (string raw in names) {
            string name = raw.Trim();
            if (name.Length >= 2 && !name.Equals("offline", StringComparison.OrdinalIgnoreCase)) {
                text = Regex.Replace(text, $@"(?<!\w){Regex.Escape(name)}(?!\w)", "<name>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }
        return text;
    }

    private static void TryDelete(FileInfo file) {
        try {
            file.Delete();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // in use or no access: the next look tries again
        }
    }
}
