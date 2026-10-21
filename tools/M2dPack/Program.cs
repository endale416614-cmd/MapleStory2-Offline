// M2dPack: write MS2 .m2d/.m2h archives using only the pack/crypto types of Maple2.File.IO (no key handling here).
//   M2dPack overlay <base.m2d> <overlayDir> <out.m2d> [--reencode]
//   M2dPack verify  <a.m2d> <b.m2d>
//   M2dPack info    <x.m2d>
//   M2dPack selftest <base.m2d>   empty overlay into a temp archive, then verify it equals the base
// Unchanged entries are copied as raw encoded blocks (original flags/bytes kept). --reencode re-encrypts every
// entry through Encode (Zlib+AES) instead (self-test of the encode path).
using System.Security.Cryptography;
using System.Text;
using Maple2.File.IO;
using Maple2.File.IO.Crypto;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.IO.Crypto.Keys;
using Maple2.File.IO.Crypto.Stream;

if (args.Length < 2) return Usage();
switch (args[0]) {
    case "overlay" when args.Length >= 4: return Overlay(args[1], args[2], args[3], args.Contains("--reencode"));
    case "verify" when args.Length == 3: return Verify(args[1], args[2]);
    case "info" when args.Length == 2: return Info(args[1]);
    case "selftest" when args.Length == 2: return SelfTest(args[1]);
    case "list" when args.Length == 2: foreach (var e in Load(args[1]).Stream.FileList) Console.Write(e.ToString()); return 0;
    default: return Usage();
}

static int Usage() { Console.Error.WriteLine("M2dPack overlay <base.m2d> <overlayDir> <out.m2d> [--reencode] | verify <a.m2d> <b.m2d> | info <x.m2d> | selftest <base.m2d>"); return 2; }

// Overlay with nothing into a temp archive, then Verify it against the base: any difference means the pack path is broken.
static int SelfTest(string baseM2d) {
    string tmp = Path.Combine(Path.GetTempPath(), "m2dpack-selftest-" + Guid.NewGuid().ToString("N"));
    string empty = Path.Combine(tmp, "overlay"), outM2d = Path.Combine(tmp, "out.m2d");
    Directory.CreateDirectory(empty);
    try { Overlay(baseM2d, empty, outM2d, false); return Verify(baseM2d, outM2d); }
    finally { Directory.Delete(tmp, true); }
}

static string H(string m2d) => Path.ChangeExtension(m2d, ".m2h");

// Mirrors M2dReader's header load but keeps the IPackStream and the raw decrypted file string.
static (IPackStream Stream, byte[] RawFileString) Load(string m2d) {
    using var br = new BinaryReader(File.OpenRead(H(m2d)));
    var stream = IPackStream.CreateStream(br);
    byte[] raw = CryptoManager.DecryptFileString(stream, br.BaseStream);
    string fileString = Encoding.UTF8.GetString(raw);
    stream.FileList.AddRange(PackFileEntry.CreateFileList(fileString));
    stream.FileList.Sort();
    byte[] table = CryptoManager.DecryptFileTable(stream, br.BaseStream);
    using var tr = new BinaryReader(new MemoryStream(table));
    stream.InitFileList(tr);
    return (stream, raw);
}

static string Norm(string name) => name.Replace('\\', '/');
static string TopOf(string normName) { int i = normName.IndexOf((char)47); return i < 0 ? "" : normName[..i].ToLowerInvariant(); }

static int Info(string m2d) {
    var (s, fs) = Load(m2d);
    Console.WriteLine($"version={s.Version} count={s.FileListCount} list={s.FileList.Count}");
    Console.WriteLine($"header: raw={s.HeaderSize} compressed={s.CompressedHeaderSize} encoded={s.EncodedHeaderSize}");
    Console.WriteLine($"table : raw={s.DataSize} compressed={s.CompressedDataSize} encoded={s.EncodedDataSize}");
    Console.WriteLine($"m2d bytes={new FileInfo(m2d).Length} m2h bytes={new FileInfo(H(m2d)).Length}");
    Console.WriteLine("flags: " + string.Join(", ", s.FileList.GroupBy(e => e.FileHeader.BufferFlag).Select(g => $"{g.Key}={g.Count()}")));
    bool seq = s.FileList.Select((e, i) => e.Index == i + 1 && e.FileHeader.FileIndex == i + 1).All(x => x);
    ulong pos = 0; bool contig = true;
    foreach (var e in s.FileList) { if (e.FileHeader.Offset != pos) { contig = false; break; } pos += e.FileHeader.EncodedFileSize; }
    Console.WriteLine($"index==position+1 for all: {seq}; data contiguous from 0 in index order: {contig} (end={pos})");
    Console.WriteLine($"fileString endsWithCRLF={(fs[^2] == 13 && fs[^1] == 10)} withHash={s.FileList.Count(e => !string.IsNullOrEmpty(e.Hash))} backslashNames={s.FileList.Count(e => e.Name.Contains('\\'))}");
    foreach (var e in s.FileList.Take(2).Concat(s.FileList.TakeLast(1))) Console.WriteLine("  " + e.ToString().TrimEnd());
    return 0;
}

static int Overlay(string baseM2d, string dir, string outM2d, bool reencode) {
    var (bs, baseRaw) = Load(baseM2d);
    var version = bs.Version;
    bool backslash = bs.FileList.Any(e => e.Name.Contains('\\'));
    var overlay = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Norm(Path.GetRelativePath(dir, p)), p => p, StringComparer.OrdinalIgnoreCase);

    var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var plan = new List<(PackFileEntry Entry, byte[]? Data)>(); // Data null = keep the original block
    foreach (var e in bs.FileList) {
        string key = Norm(e.Name);
        if (overlay.TryGetValue(key, out var path)) { replaced.Add(key); plan.Add((e, File.ReadAllBytes(path))); }
        else plan.Add((e, null));
    }
    foreach (var kv in overlay.Where(kv => !replaced.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
        string name = backslash ? kv.Key.Replace('/', '\\') : kv.Key;
        // Hash is constant per top-level folder in every base archive checked; new top-level folders get none (unknown algorithm).
        string top = TopOf(kv.Key);
        string hash = bs.FileList.FirstOrDefault(b => TopOf(Norm(b.Name)) == top)?.Hash ?? "";
        if (hash == "") Console.WriteLine("warning: no hash known for new top-level folder " + top);
        plan.Add((new PackFileEntry { Name = name, Hash = hash }, File.ReadAllBytes(kv.Value)));
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outM2d))!);
    var newEntries = new List<PackFileEntry>();
    using (var src = reencode ? null : File.OpenRead(baseM2d)) // 2.3.7 M2dReader maps the file exclusively
    using (var baseReader = reencode ? new M2dReader(baseM2d) : null)
    using (var dst = new BinaryWriter(File.Create(outM2d))) {
        var deferred = new List<(int Slot, byte[] Data)>(); // replaced entries: new data goes after all base blocks
        int i = 0;
        foreach (var (e, data) in plan) {
            i++;
            ulong offset = (ulong)dst.BaseStream.Position;
            IPackFileHeader? hdr = e.FileHeader;
            byte[] block;
            if (data == null && !reencode) {
                block = new byte[hdr!.EncodedFileSize];
                src!.Seek((long)hdr.Offset, SeekOrigin.Begin);
                src.ReadExactly(block);
                hdr.Offset = offset; hdr.FileIndex = i;
            } else if (hdr != null && !reencode) {
                // Replaced base entry: keep its original block here so the base bytes stay a prefix (append-only).
                block = new byte[hdr.EncodedFileSize];
                src!.Seek((long)hdr.Offset, SeekOrigin.Begin);
                src.ReadExactly(block);
                hdr.Offset = offset; hdr.FileIndex = i;
                deferred.Add((newEntries.Count, data!));
            } else {
                byte[] plain = data ?? baseReader!.GetBytes(e);
                var flag = hdr?.BufferFlag ?? (Encryption.Aes | Encryption.Zlib);
                block = Encode(version, plain, flag, out uint sz, out uint cz, out uint ez);
                hdr = version switch {
                    PackVersion.MS2F => PackFileHeaderVer1.CreateHeader(i, flag, offset, (byte[])plain.Clone()),
                    PackVersion.NS2F => PackFileHeaderVer2.CreateHeader(i, flag, offset, (byte[])plain.Clone()),
                    _ => PackFileHeaderVer3.CreateHeader(version, i, flag, offset, (byte[])plain.Clone()),
                };
                hdr.FileSize = sz; hdr.CompressedFileSize = cz; hdr.EncodedFileSize = ez; // CreateHeader's own sizes come from the buggy Encrypt
            }
            dst.Write(block);
            newEntries.Add(new PackFileEntry { Index = i, Hash = e.Hash, Name = e.Name, TreeName = e.TreeName, FileHeader = hdr });
        }
        
        foreach (var (slot, data) in deferred) {
            ulong offset = (ulong)dst.BaseStream.Position;
            PackFileEntry entry = newEntries[slot];
            var flag = entry.FileHeader.BufferFlag;
            byte[] block = Encode(version, data, flag, out uint sz, out uint cz, out uint ez);
            IPackFileHeader hdr = version switch {
                PackVersion.MS2F => PackFileHeaderVer1.CreateHeader(entry.Index, flag, offset, (byte[])data.Clone()),
                PackVersion.NS2F => PackFileHeaderVer2.CreateHeader(entry.Index, flag, offset, (byte[])data.Clone()),
                _ => PackFileHeaderVer3.CreateHeader(version, entry.Index, flag, offset, (byte[])data.Clone()),
            };
            hdr.FileSize = sz; hdr.CompressedFileSize = cz; hdr.EncodedFileSize = ez;
            dst.Write(block);
            entry.FileHeader = hdr;
        }
    }

    // Rebuild .m2h: version, stream sizes, encrypted file string, encrypted file table.
    // Base lines are kept as raw bytes (some names are not valid UTF-8, so a string round trip would alter them); only new lines are appended.
    byte[] listBytes = baseRaw.Concat(Encoding.UTF8.GetBytes(string.Concat(newEntries.Skip(bs.FileList.Count).Select(e => e.ToString())))).ToArray();
    bool utf8Clean = Encoding.UTF8.GetBytes(string.Concat(bs.FileList.Select(e => e.ToString()))).AsSpan().SequenceEqual(baseRaw);
    var tbl = new MemoryStream();
    using (var tw = new BinaryWriter(tbl, Encoding.UTF8, true)) foreach (var e in newEntries) e.FileHeader.Encode(tw);
    var fsBytes = Encode(version, listBytes, Encryption.Aes | Encryption.Zlib, out var hs, out var hc, out var he);
    var tbBytes = Encode(version, tbl.ToArray(), Encryption.Aes | Encryption.Zlib, out var ds, out var dc, out var de);
    bs.FileListCount = (ulong)newEntries.Count;
    bs.HeaderSize = hs; bs.CompressedHeaderSize = hc; bs.EncodedHeaderSize = he;
    bs.DataSize = ds; bs.CompressedDataSize = dc; bs.EncodedDataSize = de;
    using (var hw = new BinaryWriter(File.Create(H(outM2d)))) {
        hw.Write((uint)version);
        WriteStreamHeader(hw, bs, File.ReadAllBytes(H(baseM2d)));
        hw.Write(fsBytes);
        hw.Write(tbBytes);
    }
    Console.WriteLine($"wrote {outM2d}: {newEntries.Count} entries ({replaced.Count} replaced, {newEntries.Count - bs.FileList.Count} added, reencode={reencode}); " +
        $"base name list is valid UTF-8 (string round trip lossless)={utf8Clean}");

    // Re-read our own output and compare per-entry header fields of untouched entries with the base.
    var (os, _) = Load(outM2d);
    int bad = 0;
    for (int k = 0; k < bs.FileList.Count && !reencode; k++) {
        if (plan[k].Data != null) continue;
        var a = bs.FileList[k].FileHeader; var b = os.FileList[k].FileHeader;
        if (a.BufferFlag != b.BufferFlag || a.FileSize != b.FileSize || a.CompressedFileSize != b.CompressedFileSize || a.EncodedFileSize != b.EncodedFileSize || bs.FileList[k].Name != os.FileList[k].Name) bad++;
    }
    Console.WriteLine($"reload: count={os.FileList.Count} untouched-entry header mismatches={bad}");
    return 0;
}

// IPackStream.Encode in Maple2.File.IO 2.3.7 does not mirror ParseHeader for NS2F/OS2F/PS2F (writes the count as 8 bytes), so write
// the stream header here with the exact layout ParseHeader reads. Reserved words are copied from the base .m2h.
static void WriteStreamHeader(BinaryWriter w, IPackStream s, byte[] baseM2h) {
    switch (s.Version) {
        case PackVersion.MS2F:
            w.Write(BitConverter.ToUInt32(baseM2h, 4));
            w.Write(s.CompressedDataSize); w.Write(s.EncodedDataSize); w.Write(s.HeaderSize);
            w.Write(s.CompressedHeaderSize); w.Write(s.EncodedHeaderSize); w.Write(s.FileListCount); w.Write(s.DataSize);
            break;
        case PackVersion.NS2F:
            w.Write((uint)s.FileListCount);
            w.Write(s.CompressedDataSize); w.Write(s.EncodedDataSize); w.Write(s.HeaderSize);
            w.Write(s.CompressedHeaderSize); w.Write(s.EncodedHeaderSize); w.Write(s.DataSize);
            break;
        default: // OS2F, PS2F
            w.Write((uint)s.FileListCount); w.Write(BitConverter.ToUInt32(baseM2h, 8));
            w.Write(s.CompressedDataSize); w.Write(s.EncodedDataSize);
            w.Write(s.CompressedHeaderSize); w.Write(s.EncodedHeaderSize); w.Write(s.DataSize); w.Write(s.HeaderSize);
            break;
    }
}

static Dictionary<string, string> Hashes(string m2d) {
    using var r = new M2dReader(m2d);
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var e in r.Files) d[Norm(e.Name)] = Convert.ToHexString(SHA256.HashData(r.GetBytes(e)));
    return d;
}

static int Verify(string a, string b) {
    var ha = Hashes(a); var hb = Hashes(b);
    int same = 0, diff = 0, missing = 0, added = 0;
    foreach (var (k, v) in ha) {
        if (!hb.TryGetValue(k, out var w)) { missing++; Console.WriteLine("MISSING in b: " + k); }
        else if (v == w) same++;
        else { diff++; Console.WriteLine("DIFFERENT: " + k); }
    }
    foreach (var k in hb.Keys) if (!ha.ContainsKey(k)) { added++; Console.WriteLine("ADDED in b: " + k); }
    Console.WriteLine($"a={ha.Count} b={hb.Count} identical={same} different={diff} missing(a-only)={missing} added(b-only)={added}");
    return diff + missing + added == 0 ? 0 : 1;
}

// Zlib -> AES -> Base64 like CryptoManager.Encrypt, but AES covers the whole compressed buffer. Encrypt (2.3.7) transforms only
// `size` (uncompressed length) bytes, which leaves the tail of the stream unencrypted when compressed > uncompressed
// (tiny files; measured: 38 of 115,322 Xml.m2d entries). Compression and keys still come from the library.
static byte[] Encode(PackVersion v, byte[] plain, Encryption flag, out uint size, out uint compressed, out uint encoded) {
    if (!flag.HasFlag(Encryption.Aes)) return CryptoManager.Encrypt(v, (byte[])plain.Clone(), flag, out size, out compressed, out encoded);
    byte[] buf = CryptoManager.Encrypt(v, (byte[])plain.Clone(), flag & Encryption.Zlib, out size, out compressed, out _);
    CipherKeys.GetKeyAndIV(v, compressed, out byte[] key, out byte[] iv);
    new AESCipher(key, iv).TransformBlock(buf, 0, (uint)buf.Length, buf, 0);
    byte[] b64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(buf));
    encoded = (uint)b64.Length;
    return b64;
}
