namespace MapleStory2Offline;

/// <summary>
/// Public half of the update signing key (ECDSA P-256, SubjectPublicKeyInfo, base64). Updates are only installed when
/// their signature (zip comment) verifies against it. The private half never leaves the developer's machine; see
/// tools/sign_update.py. Replaced in 1.1.9 (the 1.1.1-1.1.8 key was lost): launchers before 1.1.9 trust only the old key,
/// so they move to 1.1.9 through the full Setup, not an update file.
/// </summary>
internal static class UpdateKey {
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEOopEbtU/63TMJp0lW7rXWGqAAnKhACiFtjV+oK+BEfgl1Icrha2qGhd5vJvlE0VlmybN3p8Rfo+wnzcOyI0eSg==";

    /// <summary>The signed bytes: a fixed header, update.json, a newline, files.json (which holds every file's sha256).</summary>
    public static byte[] Message(byte[] updateJson, byte[] filesJson) =>
        [.. "MS2OFFLINE-UPDATE-1\n"u8, .. updateJson, (byte) '\n', .. filesJson];
}
