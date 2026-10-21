using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class ProblemReportTests {
    private const string Marker = "\n... (report cut: too long)";
    private string folder = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "ProblemReportTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown() {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, true);
    }

    private static readonly string Report = string.Join("\r\n",
        "-- What happened ------------------------------------",
        "12:00:00  Launcher started",
        "",
        "-- Problems ------------------------------------",
        "* [Game ERR] x1, first at 12:00:03.000: GameSession: [socket_exception] [SendOp: 0x0021] [Offset: 4] [Hint: x] [LastPacket:",
        "      21 00 4D 00 65 00 69 00 00 00 01 02 03 04 05 06 07 08 09 0A",
        "      Known problem: Server error in a game action (see TROUBLESHOOTING.txt [T1])",
        "* [Game WRN] x2, first at 12:00:04.000: FieldManager: Npc 2000 has no navmesh",
        "",
        "-- Server log: Game (last 150 lines) ------",
        "12:00:00.000 FieldManager [INF] <1> Field started",
        "12:00:01.000  GameSession [INF] <1> Client Log: hello",
        "a second line of the same client message",
        "12:00:02.000  GameSession [WRN] <1> Something odd",
        "12:00:03.000  GameSession [ERR] <1> [socket_exception] [SendOp: 0x0021] [Offset: 4] [Hint: x] [LastPacket:",
        "21 00 4D 00 65 00 69 00 00 00 01 02 03 04 05 06 07 08 09 0A",
        "12:00:04.000  GameSession [ERR] <1> [render exception] raw client debug text",
        "12:00:05.000  GameStorage [WRN] <1> Profile conflict: Motto: proposed='my secret motto'",
        "12:00:06.000 FieldManager [INF] <1> Field closed",
        "12:00:07.000  GameSession [ERR] <1> [a long client message about an exception, shortened by the rep ...",
        "",
        "-- Server log: World (last 150 lines) ------",
        "4D 00 65 00 69 00",
        "12:00:08.000 WorldServer [INF] <1> World line",
        "",
        "-- Game client log (last 80 lines) ------",
        "MS2Log_1.txt:",
        "-- a client line that starts like a header",
        "-- Loading ---",
        "a client line after a header-like one",
        "",
        "-- Offline guard log (last 20 lines) ------",
        "guard line",
        "",
        "-- end of report --");

    [Test]
    public void ForUpload_DropsWhatTheGameClientWrote() {
        string text = ProblemReport.ForUpload(Report, []);

        Assert.Multiple(() => {
            foreach (string kept in new[] { "Launcher started", "Npc 2000 has no navmesh", "Field started", "Something odd", "Field closed",
                         "-- Server log: World", "World line", "-- Offline guard log", "guard line" }) {
                Assert.That(text, Does.Contain(kept));
            }
            foreach (string dropped in new[] { "Client Log", "a second line", "SendOp", "LastPacket", "4D 00", "raw client debug",
                         "secret motto", "shortened by the rep", "Game client log", "a client line", "Loading", "Known problem" }) {
                Assert.That(text, Does.Not.Contain(dropped));
            }
            Assert.That(text, Does.EndWith("-- end of report --"));
        });
    }

    [Test]
    public void ForUpload_DropsBeforeTheNamesAreReplaced() {
        // A player named "Game" or "Client" must not change the markers the drop rules look for.
        string text = ProblemReport.ForUpload(Report, ["Game", "Client", "Log"]);

        Assert.That(text, Does.Not.Contain("hello"));
        Assert.That(text, Does.Not.Contain("a client line"));
        Assert.That(text, Does.Contain("guard line"), "the sections after the client log stay");
    }

    [Test]
    public void ForUpload_ReplacesNamesAsWholeWords() {
        string text = ProblemReport.ForUpload("bob joined\nBOB left\n[Bob]\nBobby is here\noffline user\na", ["Bob", "offline", "a", " "]);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("<name> joined"));
            Assert.That(text, Does.Contain("<name> left"));
            Assert.That(text, Does.Contain("[<name>]"));
            Assert.That(text, Does.Contain("Bobby is here"));
            Assert.That(text, Does.Contain("offline user"));
            Assert.That(text, Does.EndWith("\na"));
            Assert.That(Regex.IsMatch(text, @"(?i)(?<!\w)bob(?!\w)"), Is.False);
        });
    }

    [Test]
    public void ForUpload_CutsALongReport() {
        string text = ProblemReport.ForUpload(new string('x', 500_000), []);

        Assert.That(text, Has.Length.EqualTo(ProblemReport.MaxReportChars + Marker.Length));
        Assert.That(text, Does.EndWith(Marker));
    }

    [Test]
    public void Note_IsRedactedNamedAndCut() {
        string note = ProblemReport.Note($"Bob saw machineId: \"0123456789abcdef\" in {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\x "
                                         + new string('y', 3_000), ["Bob"]);

        Assert.Multiple(() => {
            Assert.That(note, Does.StartWith("<name> saw machineId: \"<redacted>\" in %USERPROFILE%\\x"));
            Assert.That(note, Has.Length.EqualTo(ProblemReport.MaxNoteChars));
        });
    }

    [Test]
    public void Build_WritesTheGzipJsonOfSchema1() {
        var id = Guid.NewGuid();
        byte[] payload = ProblemReport.Build(id, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc), "manual", "0123abcd",
            "windows", "1.2.0", "Bob crashed", "Bob " + new string('y', 3_000), ["Bob"]);

        using JsonDocument json = Parse(payload);
        JsonElement root = json.RootElement;
        Assert.Multiple(() => {
            Assert.That(root.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(new[] { "schema", "reportId", "installId", "createdUtc", "kind", "platform", "launcher", "note", "report" }));
            Assert.That(root.GetProperty("schema").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("reportId").GetString(), Is.EqualTo(id.ToString("D")));
            Assert.That(root.GetProperty("installId").GetString(), Is.EqualTo("0123abcd"));
            Assert.That(root.GetProperty("createdUtc").GetString(), Is.EqualTo("2026-10-21T10:00:00.0000000Z"));
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("manual"));
            Assert.That(root.GetProperty("platform").GetString(), Is.EqualTo("windows"));
            Assert.That(root.GetProperty("launcher").GetString(), Is.EqualTo("1.2.0"));
            Assert.That(root.GetProperty("report").GetString(), Is.EqualTo("<name> crashed"));
            Assert.That(root.GetProperty("note").GetString(), Has.Length.EqualTo(ProblemReport.MaxNoteChars).And.StartWith("<name> y"));
        });

        using JsonDocument noNote = Parse(ProblemReport.Build(id, DateTime.UtcNow, "problem", "x", "windows", "1.2.0", "r", null, []));
        Assert.That(noNote.RootElement.GetProperty("note").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Names_ReadsEverySaveAndSaysWhenOneCannotBeRead() {
        string saveData = Path.Combine(folder, "SaveData");
        Save(Path.Combine(saveData, "game-server.db"), "Mei", "player1");
        Save(Path.Combine(saveData, "Alt Save", "game-server.db"), "Rook", "offline");
        Directory.CreateDirectory(Path.Combine(saveData, "New Game"));
        using (var empty = new SqliteConnection($"Data Source={Path.Combine(saveData, "New Game", "game-server.db")};Pooling=False")) {
            empty.Open(); // a save the server has not filled yet
        }

        (List<string> names, string? unreadable) = ProblemReport.Names(folder);
        Assert.Multiple(() => {
            Assert.That(unreadable, Is.Null);
            Assert.That(names, Is.SupersetOf(new[] { "Mei", "player1", "Rook", "Alt Save", "New Game", Environment.UserName, Environment.MachineName }));
            Assert.That(names, Does.Not.Contain("backups"));
        });

        Directory.CreateDirectory(Path.Combine(saveData, "Broken"));
        string broken = Path.Combine(saveData, "Broken", "game-server.db");
        File.WriteAllText(broken, "not a database at all, just text that is long enough");
        Assert.Multiple(() => {
            Assert.That(ProblemReport.Names(folder).Unreadable, Is.EqualTo(broken), "a save that cannot be read");
            Assert.That(ProblemReport.SaveLabel(folder, broken), Is.EqualTo("Broken"));
            Assert.That(ProblemReport.SaveLabel(folder, Path.Combine(saveData, "game-server.db")), Is.EqualTo("main save"));
            Assert.That(ProblemReport.SaveLabel(folder, saveData), Is.EqualTo("save folder"));
            Assert.That(ProblemReport.Names(Path.Combine(folder, "no such folder")).Unreadable, Is.Null, "no save yet");
        });
    }

    [Test]
    public void Pending_KeepsTheNewest20Within30DaysOldestFirst() {
        DateTime now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var reports = new List<string>();
        for (int i = 0; i < 22; i++) {
            reports.Add(Write($"{i:D2}.problem.json.gz", now.AddHours(i - 22))); // 00 is the oldest
        }
        string old = Write("old.problem.json.gz", now.AddDays(-31));
        string leftover = Write("x.problem.json.gz.part", now.AddHours(-2));
        string writing = Write("y.problem.json.gz.part", now);

        List<string> pending = ProblemReport.Pending(folder, now);

        Assert.Multiple(() => {
            Assert.That(pending, Is.EqualTo(reports.Skip(2)));
            Assert.That(File.Exists(reports[0]) || File.Exists(reports[1]), Is.False, "beyond the newest 20");
            Assert.That(File.Exists(old), Is.False, "older than 30 days");
            Assert.That(File.Exists(leftover), Is.False, "an interrupted write");
            Assert.That(File.Exists(writing), Is.True, "a write in progress");
        });
    }

    [Test]
    public void Enqueue_KeepsTheNewReportAndWithdrawTakesBackOnlyTheGivenKinds() {
        var id = Guid.NewGuid();
        string manual = ProblemReport.Enqueue(folder, id, "manual", [1, 2, 3], DateTime.UtcNow);
        string problem = ProblemReport.Enqueue(folder, Guid.NewGuid(), "problem", [4], DateTime.UtcNow);
        string crash = ProblemReport.Enqueue(folder, Guid.NewGuid(), "crash", [5], DateTime.UtcNow);

        Assert.That(manual, Is.EqualTo(Path.Combine(folder, $"{id:D}.manual.json.gz")));
        Assert.That(File.ReadAllBytes(manual), Is.EqualTo(new byte[] { 1, 2, 3 }));
        ProblemReport.Withdraw(folder, "problem", "crash");
        Assert.That(ProblemReport.Pending(folder, DateTime.UtcNow), Is.EqualTo(new[] { manual }));
        Assert.That(File.Exists(problem) || File.Exists(crash), Is.False);
    }

    [Test]
    public async Task SendAsync_DeletesSentAndRejectedReportsAndStopsAtOtherAnswers() {
        DateTime now = DateTime.UtcNow;
        string first = Write("a.json.gz", now.AddMinutes(-4)), second = Write("b.json.gz", now.AddMinutes(-3));
        string third = Write("c.json.gz", now.AddMinutes(-2)), fourth = Write("d.json.gz", now.AddMinutes(-1));
        var service = new FakeService(HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.ServiceUnavailable);
        var sent = new HashSet<string>();

        int count = await ProblemReport.SendAsync(folder, "http://test.local/", service, now, CancellationToken.None, sent);

        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(sent, Is.EquivalentTo(new[] { first }));
            Assert.That(File.Exists(first), Is.False, "sent");
            Assert.That(File.Exists(second) || File.Exists(third), Is.False, "rejected");
            Assert.That(File.Exists(fourth), Is.True, "kept for the next time");
            Assert.That(service.Requests, Has.Count.EqualTo(4));
            Assert.That(service.Requests[0].Uri, Is.EqualTo("http://test.local/v1/report"));
            Assert.That(service.Requests[0].Encoding, Is.EqualTo("gzip"));
            Assert.That(service.Requests[0].Body, Is.EqualTo("a.json.gz"u8.ToArray()));
        });
    }

    [Test]
    public async Task SendAsync_KeepsEverythingWhenTheServiceIsUnavailableUnreachableOrSlow() {
        DateTime now = DateTime.UtcNow;
        string first = Write("a.json.gz", now.AddMinutes(-2)), second = Write("b.json.gz", now.AddMinutes(-1));

        var busy = new FakeService(HttpStatusCode.ServiceUnavailable, HttpStatusCode.Created);
        Assert.That(await ProblemReport.SendAsync(folder, "http://test.local", busy, now, CancellationToken.None), Is.EqualTo(0));
        Assert.That(busy.Requests, Has.Count.EqualTo(1), "stops at the first answer that is not final");

        foreach (Exception failure in new Exception[] { new HttpRequestException("no network"), new TaskCanceledException("timeout") }) {
            var broken = new FakeService { Failure = failure };
            Assert.That(await ProblemReport.SendAsync(folder, "http://test.local", broken, now, CancellationToken.None), Is.EqualTo(0));
        }
        Assert.That(File.Exists(first) && File.Exists(second), Is.True);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => ProblemReport.SendAsync(folder, "http://test.local", busy, now, cancelled.Token));
    }

    [Test]
    public async Task SendAsync_WithoutAServiceSendsNothing() {
        Write("a.json.gz", DateTime.UtcNow);
        var service = new FakeService(HttpStatusCode.Created);

        Assert.That(await ProblemReport.SendAsync(folder, null, service, DateTime.UtcNow, CancellationToken.None), Is.EqualTo(0));
        Assert.That(service.Requests, Is.Empty);
    }

    [Test]
    public void Url_TakesTheTestOverrideFirstAndNeverPlainHttpFromTheBuild() {
        string? before = Environment.GetEnvironmentVariable("MS2OFFLINE_REPORT_URL");
        try {
            Environment.SetEnvironmentVariable("MS2OFFLINE_REPORT_URL", "http://127.0.0.1:9");
            Assert.That(ProblemReport.Url(), Is.EqualTo("http://127.0.0.1:9"));
            Environment.SetEnvironmentVariable("MS2OFFLINE_REPORT_URL", null);
            Assert.That(ProblemReport.Url(), ProblemReport.ServiceUrl.StartsWith("https://") ? Is.EqualTo(ProblemReport.ServiceUrl) : Is.Null);
        } finally {
            Environment.SetEnvironmentVariable("MS2OFFLINE_REPORT_URL", before);
        }
    }

    private string Write(string name, DateTime timeUtc) {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, timeUtc);
        return path;
    }

    private static void Save(string path, string character, string account) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE \"character\" (Name TEXT); CREATE TABLE account (Username TEXT); " +
                              "INSERT INTO \"character\" VALUES ($c); INSERT INTO account VALUES ($a);";
        command.Parameters.AddWithValue("$c", character);
        command.Parameters.AddWithValue("$a", account);
        command.ExecuteNonQuery();
    }

    private static JsonDocument Parse(byte[] payload) {
        using var gzip = new GZipStream(new MemoryStream(payload), CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    /// <summary>Answers each request with the next status code and records it; Failure is thrown instead when set.</summary>
    private sealed class FakeService(params HttpStatusCode[] answers) : HttpMessageHandler {
        private readonly Queue<HttpStatusCode> queue = new(answers);
        public List<(string Uri, string? Encoding, byte[] Body)> Requests { get; } = [];
        public Exception? Failure { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (Failure != null) {
                throw Failure;
            }
            Requests.Add((request.RequestUri!.ToString(), request.Content!.Headers.ContentEncoding.FirstOrDefault(),
                await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(queue.Dequeue());
        }
    }
}
