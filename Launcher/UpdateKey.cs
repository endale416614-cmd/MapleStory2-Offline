namespace MapleStory2Offline;

/// <summary>
/// Public half of the update signing key (ECDSA P-256, SubjectPublicKeyInfo, base64). Updates are only installed when
/// their update.sig verifies against it. The private half never leaves the developer's machine; see docs/RELEASING.md.
/// </summary>
internal static class UpdateKey {
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAFueViYFwAszXxC7SQQ85ISHP0OoWQaBs3V/fC2iRMZHt2u9nc+mTgtppkyqVfQe6T5cykKt6bcrcF/M8DW4Xw==";

    /// <summary>The signed bytes: a fixed header, update.json, a newline, files.json (which holds every file's sha256).</summary>
    public static byte[] Message(byte[] updateJson, byte[] filesJson) =>
        [.. "MS2OFFLINE-UPDATE-1\n"u8, .. updateJson, (byte) '\n', .. filesJson];
}
