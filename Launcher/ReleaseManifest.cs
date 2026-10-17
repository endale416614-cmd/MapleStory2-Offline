using System.Security.Cryptography;
using System.Text.Json;

namespace MapleStory2Offline;

/// <summary>
/// release.json: every file of a release (Setup, full zip, update zip) with its size and sha256, published next to them with
/// release.json.sig, the base64 ECDSA P-256 / SHA-256 signature (update key, <see cref="UpdateKey"/>) over
/// "MS2OFFLINE-RELEASE-1\n" + the release.json bytes. Optional: a release without release.json is handled as before; one
/// with it is only used when the signature verifies, and its update file must match the listed sha256.
/// </summary>
internal sealed class ReleaseManifest {
    public const string FileName = "release.json";
    public const string SignatureName = "release.json.sig";
    public const int MaxBytes = 1 << 20;

    public sealed record Entry(string Name, long Size, string Sha256);

    public string Version { get; }
    public IReadOnlyList<Entry> Files { get; }

    private ReleaseManifest(string version, IReadOnlyList<Entry> files) {
        Version = version;
        Files = files;
    }

    /// <summary>The signed bytes. The header differs from the update signature's, so neither signature fits the other file.</summary>
    public static byte[] Message(byte[] json) => [.. "MS2OFFLINE-RELEASE-1\n"u8, .. json];

    /// <summary>Checks the signature, then parses. Throws <see cref="InvalidDataException"/> unless both are valid.</summary>
    public static ReleaseManifest Verify(byte[] json, byte[] signature, string publicKey = UpdateKey.PublicKey) {
        bool valid;
        try {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            valid = key.VerifyData(Message(json), Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(signature).Trim()),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        } catch (Exception ex) when (ex is FormatException or CryptographicException) {
            valid = false;
        }
        if (!valid) {
            throw new InvalidDataException($"{FileName} is not signed by the MapleStory 2 Offline developer, or it was changed after signing");
        }
        return Parse(json);
    }

    /// <summary>The listed file with this name, or null.</summary>
    public Entry? Find(string name) => Files.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    // Strict: anything unexpected in a signed file is a packaging error, not something to guess around.
    private static ReleaseManifest Parse(byte[] json) {
        try {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string version = root.GetProperty("version").GetString() ?? "";
            if (version.Length == 0) {
                throw new InvalidDataException($"{FileName} has no version");
            }
            var files = new List<Entry>();
            foreach (JsonElement file in root.GetProperty("files").EnumerateArray()) {
                string name = file.GetProperty("name").GetString() ?? "";
                long size = file.GetProperty("size").GetInt64();
                string sha256 = (file.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
                if (name.Trim('.').Length == 0 || name != Path.GetFileName(name) || name.IndexOfAny(['/', '\\', .. Path.GetInvalidFileNameChars()]) >= 0) {
                    throw new InvalidDataException($"{FileName} lists an invalid file name");
                }
                if (size < 0 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) {
                    throw new InvalidDataException($"{FileName} lists {name} without a valid size or sha256");
                }
                if (files.Any(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidDataException($"{FileName} lists {name} twice");
                }
                files.Add(new Entry(name, size, sha256));
            }
            return new ReleaseManifest(version, files);
        } catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) {
            throw new InvalidDataException($"{FileName} is not readable: {ex.Message}");
        }
    }
}
