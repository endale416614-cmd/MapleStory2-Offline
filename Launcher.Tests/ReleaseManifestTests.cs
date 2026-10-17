using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class ReleaseManifestTests {
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly string Json = $$"""
        {"version": "1.2.0", "notes": "", "files": [
         {"name": "MapleStory2-Offline-Setup-1.2.0.exe", "size": 10, "sha256": "{{Sha}}"},
         {"name": "MapleStory2-Offline-Update-1.2.0.zip", "size": 20, "sha256": "{{Sha.ToUpperInvariant()}}"}]}
        """;

    private ECDsa key = null!;
    private string publicKey = null!;

    [SetUp]
    public void SetUp() {
        key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    [TearDown]
    public void TearDown() => key.Dispose();

    // What tools/sign_update.py --manifest writes into release.json.sig.
    private byte[] Sign(byte[] message, ECDsa? with = null) =>
        Encoding.ASCII.GetBytes(Convert.ToBase64String((with ?? key).SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) + "\n");

    [Test]
    public void SignedManifest_Verifies() {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        ReleaseManifest manifest = ReleaseManifest.Verify(json, Sign(ReleaseManifest.Message(json)), publicKey);

        Assert.That(manifest.Version, Is.EqualTo("1.2.0"));
        Assert.That(manifest.Files, Has.Count.EqualTo(2));
        ReleaseManifest.Entry? update = manifest.Find("mapleStory2-offline-update-1.2.0.ZIP");
        Assert.That(update, Is.Not.Null);
        Assert.That(update!.Size, Is.EqualTo(20));
        Assert.That(update.Sha256, Is.EqualTo(Sha)); // normalised to lower case
        Assert.That(manifest.Find("other.zip"), Is.Null);
    }

    [Test]
    public void ChangedByte_IsRefused() {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        byte[] signature = Sign(ReleaseManifest.Message(json));
        json[json.Length / 2] ^= 1;
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, signature, publicKey));
    }

    [Test]
    public void OtherKey_IsRefused() {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, Sign(ReleaseManifest.Message(json), other), publicKey));
    }

    [Test]
    public void SignatureWithoutReleaseHeader_IsRefused() {
        // A signature over the bare bytes, or over the update header, must not pass as a release signature.
        byte[] json = Encoding.UTF8.GetBytes(Json);
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, Sign(json), publicKey));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, Sign([.. "MS2OFFLINE-UPDATE-1\n"u8, .. json]), publicKey));
    }

    [TestCase("")]
    [TestCase("not base64!")]
    [TestCase("AAAA")]
    public void GarbageSignature_IsRefused(string signature) {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, Encoding.ASCII.GetBytes(signature), publicKey));
    }

    [Test]
    public void BuiltInKey_Loads() {
        // The default key must import; a wrong signature is then an ordinary refusal, not a crash.
        byte[] json = Encoding.UTF8.GetBytes(Json);
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, Sign(ReleaseManifest.Message(json))));
    }

    [TestCase("""{"version": "", "files": []}""")]
    [TestCase("""{"files": []}""")]
    [TestCase("""{"version": "1.2.0"}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "..\\x.zip", "size": 1, "sha256": "SHA"}]}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "dir/x.zip", "size": 1, "sha256": "SHA"}]}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "x.zip", "size": -1, "sha256": "SHA"}]}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "x.zip", "size": "1", "sha256": "SHA"}]}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "x.zip", "size": 1, "sha256": "abc"}]}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "x.zip", "size": 1, "sha256": "SHA"}, {"name": "X.ZIP", "size": 1, "sha256": "SHA"}]}""")]
    [TestCase("""{"version": "1.2.0", "files": [{"name": "..", "size": 1, "sha256": "SHA"}]}""")]
    [TestCase("""not json""")]
    public void SignedButInvalidContent_IsRefused(string template) {
        byte[] json = Encoding.UTF8.GetBytes(template.Replace("SHA", Sha));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Verify(json, Sign(ReleaseManifest.Message(json)), publicKey));
    }
}
