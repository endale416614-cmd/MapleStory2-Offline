using System.Text.Json;

namespace MapleStory2Offline;

/// <summary>
/// Online update check (launcher.ini AutoUpdate): asks GitHub for the latest release of this project and downloads its
/// update file into the updates folder, where <see cref="UpdateInstaller"/> verifies and installs it like a manual
/// update. Only the launcher goes online, only for this, and any failure (no internet, timeout, unexpected answer)
/// just means "no update" - the game always starts. Nothing about the player or the PC is sent.
/// </summary>
internal static class UpdateChecker {
    public const string Repository = "endale416614-cmd/MapleStory2-Offline";
    private const string AssetPrefix = "MapleStory2-Offline-Update-";
    private const long MaxDownloadBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Tests point this at a local server (MS2OFFLINE_UPDATE_FEED); players always use GitHub.</summary>
    private static string Feed => Environment.GetEnvironmentVariable("MS2OFFLINE_UPDATE_FEED") is { Length: > 0 } feed
        ? feed
        : $"https://api.github.com/repos/{Repository}/releases/latest";

    public sealed record Available(string Version, string Notes, string PageUrl, string? FileUrl, string? FileName, long Size);

    /// <summary>The newer release, or null when this version is current. Throws when the check could not be made.</summary>
    public static async Task<Available?> CheckAsync(Settings settings, CancellationToken token) {
        using HttpClient http = Client(TimeSpan.FromSeconds(6), settings);
        using JsonDocument release = JsonDocument.Parse(await http.GetStringAsync(Feed, token));
        JsonElement root = release.RootElement;

        string version = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
        if (UpdateInstaller.ParseVersion(version) <= UpdateInstaller.ParseVersion(settings.Version)) {
            return null;
        }

        string? fileUrl = null, fileName = null;
        long size = 0;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray()) {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                // Prefer the file named for this release; otherwise the first one listed.
                bool forThisRelease = name.Contains(version, StringComparison.OrdinalIgnoreCase);
                if (fileUrl == null || forThisRelease) {
                    fileUrl = asset.GetProperty("browser_download_url").GetString();
                    fileName = Path.GetFileName(name);
                    size = asset.GetProperty("size").GetInt64();
                }
                if (forThisRelease) {
                    break;
                }
            }
        }
        if (fileUrl != null && !Trusted(fileUrl)) {
            throw new InvalidOperationException($"unexpected download address {fileUrl}");
        }
        if (size > MaxDownloadBytes) {
            throw new InvalidOperationException($"update file too large ({size} bytes)");
        }

        string page = root.TryGetProperty("html_url", out JsonElement html) ? html.GetString() ?? "" : "";
        return new Available(version, Summary(root.TryGetProperty("body", out JsonElement body) ? body.GetString() : null), page, fileUrl, fileName, size);
    }

    /// <summary>Downloads the update file into <paramref name="folder"/> (via a .part file); returns its path.</summary>
    public static async Task<string> DownloadAsync(Available update, Settings settings, string folder, IProgress<int>? percent, CancellationToken token) {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, update.FileName!), part = target + ".part";
        using HttpClient http = Client(Timeout.InfiniteTimeSpan, settings);
        using HttpResponseMessage response = await http.GetAsync(update.FileUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        // The size the release lists for the file is the limit; the server's own length header is not trusted.
        long limit = update.Size > 0 ? Math.Min(update.Size, MaxDownloadBytes) : MaxDownloadBytes;
        if (response.Content.Headers.ContentLength is long length && length > limit) {
            throw new InvalidOperationException("the download is larger than announced");
        }
        try {
            await using (Stream source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None)) {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read, last = -1;
                while ((read = await source.ReadAsync(buffer, token)) > 0) {
                    done += read;
                    if (done > limit) {
                        throw new InvalidOperationException("the download is larger than announced");
                    }
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    int now = (int) (done * 100 / limit);
                    if (now != last) {
                        percent?.Report(last = now);
                    }
                }
                if (update.Size > 0 && done != update.Size) {
                    throw new InvalidOperationException("the download is incomplete");
                }
            }
            File.Move(part, target, true);
            return target;
        } catch {
            File.Delete(part);
            throw;
        }
    }

    private static HttpClient Client(TimeSpan timeout, Settings settings) {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MapleStory2-Offline-Launcher/{UpdateInstaller.ParseVersion(settings.Version)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    // Downloads only come from GitHub (or from the test feed's own host).
    private static bool Trusted(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) {
            return false;
        }
        if (Environment.GetEnvironmentVariable("MS2OFFLINE_UPDATE_FEED") is { Length: > 0 } feed) {
            return Uri.TryCreate(feed, UriKind.Absolute, out Uri? feedUri) && uri.Host == feedUri.Host;
        }
        return uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "github.com" || uri.Host.EndsWith(".githubusercontent.com"));
    }

    // The bullet points of the release notes (what changed), at most eight.
    private static string Summary(string? body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return "";
        }
        IEnumerable<string> bullets = body.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("- ") || line.StartsWith("* "))
            .Select(line => "• " + line[2..].Replace("**", ""))
            .Take(8);
        return string.Join("\n", bullets);
    }
}
