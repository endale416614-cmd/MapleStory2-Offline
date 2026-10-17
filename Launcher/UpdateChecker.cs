using System.Security.Cryptography;
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

    /// <summary><paramref name="Sha256"/> is set when the release has a signed release.json; the download must match it.</summary>
    public sealed record Available(string Version, string Notes, string PageUrl, string? FileUrl, string? FileName, long Size, string? Sha256 = null);

    /// <summary>The newer release, or null when this version is current. Throws when the check could not be made.</summary>
    public static async Task<Available?> CheckAsync(Settings settings, CancellationToken token) {
        using HttpClient http = Client(TimeSpan.FromSeconds(6), settings);
        using JsonDocument release = JsonDocument.Parse(await http.GetStringAsync(Feed, token));
        JsonElement root = release.RootElement;

        string version = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
        if (UpdateInstaller.ParseVersion(version) <= UpdateInstaller.ParseVersion(settings.Version)) {
            return null;
        }

        string? fileUrl = null, fileName = null, manifestUrl = null, signatureUrl = null;
        long size = 0;
        bool chosenForThisRelease = false;
        // Every asset is looked at (no early exit): release.json may be listed after the update file.
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray()) {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name.Equals(ReleaseManifest.FileName, StringComparison.OrdinalIgnoreCase)) {
                manifestUrl = asset.GetProperty("browser_download_url").GetString();
            } else if (name.Equals(ReleaseManifest.SignatureName, StringComparison.OrdinalIgnoreCase)) {
                signatureUrl = asset.GetProperty("browser_download_url").GetString();
            } else if (!chosenForThisRelease && name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                // Prefer the first file named for this release; otherwise the first one listed.
                bool forThisRelease = name.Contains(version, StringComparison.OrdinalIgnoreCase);
                if (fileUrl == null || forThisRelease) {
                    fileUrl = asset.GetProperty("browser_download_url").GetString();
                    fileName = Path.GetFileName(name);
                    size = asset.GetProperty("size").GetInt64();
                    chosenForThisRelease = forThisRelease;
                }
            }
        }
        if (fileUrl != null && !Trusted(fileUrl)) {
            throw new InvalidOperationException($"unexpected download address {fileUrl}");
        }
        if (size > MaxDownloadBytes) {
            throw new InvalidOperationException($"update file too large ({size} bytes)");
        }

        // Releases from before 1.2.0 have no release.json; their update file is still checked by its own signature.
        string? sha256 = null;
        if (manifestUrl != null) {
            if (signatureUrl == null) {
                throw new InvalidOperationException($"{ReleaseManifest.FileName} of {version} is not signed; update refused");
            }
            if (!Trusted(manifestUrl) || !Trusted(signatureUrl)) {
                throw new InvalidOperationException($"unexpected download address for {ReleaseManifest.FileName}");
            }
            ReleaseManifest manifest;
            try {
                manifest = ReleaseManifest.Verify(await GetSmallAsync(settings, manifestUrl, token), await GetSmallAsync(settings, signatureUrl, token));
            } catch (InvalidDataException ex) {
                throw new InvalidOperationException($"{ex.Message}; update {version} refused");
            }
            if (manifest.Version != version) {
                throw new InvalidOperationException($"{ReleaseManifest.FileName} is for version {manifest.Version}, not {version}; update refused");
            }
            if (fileName != null) {
                ReleaseManifest.Entry entry = manifest.Find(fileName)
                    ?? throw new InvalidOperationException($"{fileName} is not listed in {ReleaseManifest.FileName}; update refused");
                if (entry.Size != size) {
                    throw new InvalidOperationException($"{fileName} has a different size than {ReleaseManifest.FileName} lists; update refused");
                }
                sha256 = entry.Sha256;
            }
        }

        string page = root.TryGetProperty("html_url", out JsonElement html) ? html.GetString() ?? "" : "";
        return new Available(version, Summary(root.TryGetProperty("body", out JsonElement body) ? body.GetString() : null), page, fileUrl, fileName, size, sha256);
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
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read, last = -1;
                while ((read = await source.ReadAsync(buffer, token)) > 0) {
                    done += read;
                    if (done > limit) {
                        throw new InvalidOperationException("the download is larger than announced");
                    }
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    hash.AppendData(buffer, 0, read);
                    int now = (int) (done * 100 / limit);
                    if (now != last) {
                        percent?.Report(last = now);
                    }
                }
                if (update.Size > 0 && done != update.Size) {
                    throw new InvalidOperationException("the download is incomplete");
                }
                if (update.Sha256 != null && Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != update.Sha256) {
                    throw new InvalidOperationException($"the download does not match the signed {ReleaseManifest.FileName}");
                }
            }
            File.Move(part, target, true);
            return target;
        } catch {
            File.Delete(part);
            throw;
        }
    }

    // release.json and its signature: small files, read whole; the timeout covers the body and the size is capped.
    private static async Task<byte[]> GetSmallAsync(Settings settings, string url, CancellationToken token) {
        using HttpClient http = Client(TimeSpan.FromSeconds(10), settings);
        http.MaxResponseContentBufferSize = ReleaseManifest.MaxBytes;
        return await http.GetByteArrayAsync(url, token);
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
