#!/usr/bin/env python3
"""Reads MapleShark2 .msb packet captures with the standard library: an opcode tally, an index and the ExpUp decoder.

  py -3 -I tools/msb.py scan <captures> [--check]    writes Maple2/capture-opcode-tally.tsv and <captures>/index.tsv
  py -3 -I tools/msb.py expup <file.msb>             prints the capture's ExpUp packets
  py -3 -I tools/msb.py --selftest                   checks the rules on a synthetic capture

<captures> holds loose/, orion2/ and doll/ (the files the server fidelity map counts) and inner/ (what the archives
inside them unpacked to). Captures hold player names and addresses, so they stay outside the repository. The tally
holds op names and counts only. The index holds file names, so it stays beside the captures.

The format, as the captures' own bytes show it (versions 0x2027 and 0x2030):
  u16 version, string local address, u16 port, string remote address, u16 port, u8 locale, u32 build, then records to
  the end of the file: i64 ticks, i32 size, u16 opcode, u8 outbound, size bytes of payload; 0x2027 adds 8 bytes to each.
A string is a 7-bit length, as .NET writes it, and that many bytes.
"""

import argparse
import collections
import datetime
import hashlib
import os
import re
import struct
import sys
import tempfile

GMS_BUILD = 12  # TODO: FT-34: the KMS builds need an opcode map; until then their ops stay hex and nothing reads them
EXTRA_BYTES = {0x2027: 8, 0x2030: 0}
RECORD = struct.Struct("<qiHB")
EXP_UP = 0x0038  # SendOp.ExpUp
REQUEST_FIELD_ENTER = 0x0016  # SendOp.RequestFieldEnter
EXP_UP_ADD = struct.Struct("<BqHqqiB")
EXP_UP_REST = struct.Struct("<Bq")
MAPLE2 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Maple2")
TALLY = os.path.join(MAPLE2, "capture-opcode-tally.tsv")
OPS = os.path.join(MAPLE2, "Maple2.Server.Core", "Constants")
MAP_GROUPS = ("loose", "orion2", "doll")
TALLY_HEADER = ("direction", "op", "map_files", "all_files", "unique_files")
INDEX_HEADER = ("file", "system", "sha256", "duplicate_of", "version", "build", "packets", "start", "end", "maps",
                "server_ops", "client_ops")

Record = collections.namedtuple("Record", "ticks op outbound payload")
Capture = collections.namedtuple("Capture", "version build records")


def skip_string(data, offset):
    length = shift = 0
    while True:
        byte = data[offset]
        offset += 1
        length |= (byte & 0x7F) << shift
        shift += 7
        if byte < 0x80:
            return offset + length


def parse_msb(data):
    """The version, build and records of a capture. The addresses are skipped, never returned. A file that does not
    read exactly to its end raises ValueError, so a damaged capture can not shrink a count."""
    try:
        version = struct.unpack_from("<H", data, 0)[0]
        if version not in EXTRA_BYTES:
            raise ValueError(f"unknown version 0x{version:04X}")
        extra = EXTRA_BYTES[version]
        offset = skip_string(data, skip_string(data, 2) + 2) + 2
        build = struct.unpack_from("<I", data, offset + 1)[0]
        offset += 5
        records = []
        while offset < len(data):
            ticks, size, op, outbound = RECORD.unpack_from(data, offset)
            offset += RECORD.size
            if size < 0 or offset + size + extra > len(data):
                raise ValueError(f"a record at offset {offset - RECORD.size} runs past the end")
            records.append(Record(ticks, op, outbound != 0, data[offset:offset + size]))
            offset += size + extra
    except (IndexError, struct.error) as error:
        raise ValueError(f"truncated: {error}") from error
    return Capture(version, build, records)


def read_msb(path):
    with open(path, "rb") as f:
        return parse_msb(f.read())


def op_names(path):
    """opcode -> name of a SendOp.cs or RecvOp.cs. A line commented out names nothing."""
    with open(path, encoding="utf-8-sig") as f:
        members = re.findall(r"^\s*(\w+)\s*=\s*0x([0-9A-Fa-f]+)", f.read(), re.MULTILINE)
    return {int(value, 16): name for name, value in members}


def decode_exp_up(payload):
    """ExpUp, as ExperienceUpPacket.cs writes it. Command 0: gained, message, total, rest, parameter, additional.
    Command 1: rest. Anything else raises ValueError."""
    if payload[:1] == b"\x00" and len(payload) == EXP_UP_ADD.size:
        _, gained, message, total, rest, parameter, additional = EXP_UP_ADD.unpack(payload)
        return {"command": 0, "gained": gained, "message": message, "total": total, "rest": rest,
                "parameter": parameter, "additional": additional != 0}
    if payload[:1] == b"\x01" and len(payload) == EXP_UP_REST.size:
        return {"command": 1, "rest": EXP_UP_REST.unpack(payload)[1]}
    raise ValueError(f"not an ExpUp packet: {len(payload)} bytes")


def field_maps(records):
    """The map ids of a GMS capture's RequestFieldEnter packets, each once, in order. An error reply is one byte."""
    maps = [struct.unpack_from("<i", record.payload, 1)[0] for record in records
            if record.op == REQUEST_FIELD_ENTER and not record.outbound and len(record.payload) >= 5
            and record.payload[0] == 0]
    return list(dict.fromkeys(maps))


def system_of(rel):
    """The topic a capture's path names: a loose file's first folder, a nested file's archive, else its own name. A
    label from the path, not a classification. Uploads added -XXXXX to the names."""
    parts = rel.split("/")
    name = parts[1] if parts[0] in ("loose", "inner") and len(parts) > 2 else parts[-1]
    return re.sub(r"(-[0-9A-F]{5})?(\.(?i:rar|zip|msb))?$", "", name, count=1)


def stamp(ticks):
    return (datetime.datetime(1, 1, 1) + datetime.timedelta(microseconds=ticks // 10)).isoformat(" ", "seconds")


def scan(root, send, recv):
    """The index rows of every .msb under root, and the tally: (direction, op) -> counts over the map's GMS files, all
    GMS files and the GMS files that are no byte copy of an earlier one."""
    rows = []
    tally = collections.defaultdict(lambda: [0, 0, 0])
    first = {}
    files = collections.Counter()
    paths = [os.path.relpath(os.path.join(folder, name), root).replace(os.sep, "/")
             for folder, _, names in os.walk(root) for name in names if name.lower().endswith(".msb")]
    # The map's files come first, so a nested file is the copy when one of them holds the same bytes.
    for rel in sorted(paths, key=lambda path: (path.split("/")[0] not in MAP_GROUPS, path)):
        with open(os.path.join(root, rel), "rb") as f:
            data = f.read()
        try:
            capture = parse_msb(data)
        except ValueError as error:
            raise ValueError(f"{rel}: {error}") from error
        sha = hashlib.sha256(data).hexdigest()
        duplicate = first.setdefault(sha, rel)
        duplicate = "" if duplicate == rel else duplicate
        gms = capture.build == GMS_BUILD
        in_map = gms and rel.split("/")[0] in MAP_GROUPS
        ops = collections.Counter()
        for record in capture.records:
            name = (recv if record.outbound else send).get(record.op) if gms else None
            ops["C" if record.outbound else "S", name or f"0x{record.op:04X}"] += 1
        if gms:
            for key, count in ops.items():
                tally[key][0] += count if in_map else 0
                tally[key][1] += count
                tally[key][2] += count if not duplicate else 0
        files.update(all=1, unique=not duplicate, gms=gms, map=in_map, gms_unique=gms and not duplicate,
                     map_unique=in_map and not duplicate)
        by_count = sorted(ops.items(), key=lambda item: (-item[1], item[0]))
        rows.append((rel, system_of(rel), sha, duplicate, f"0x{capture.version:04X}", str(capture.build),
                     str(len(capture.records)),
                     stamp(capture.records[0].ticks) if capture.records else "",
                     stamp(capture.records[-1].ticks) if capture.records else "",
                     ",".join(map(str, field_maps(capture.records))) if gms else "",
                     " ".join(f"{op}:{count}" for (side, op), count in by_count if side == "S"),
                     " ".join(f"{op}:{count}" for (side, op), count in by_count if side == "C")))
    return rows, tally, files


def tally_text(tally):
    rows = sorted(tally.items(), key=lambda item: (item[0][0] != "S", -item[1][0], -item[1][2], item[0][1]))
    lines = [TALLY_HEADER] + [(side, op, *map(str, counts)) for (side, op), counts in rows]
    return "".join("\t".join(line) + "\n" for line in lines)


def index_text(rows):
    return "".join("\t".join(row) + "\n" for row in [INDEX_HEADER] + rows)


def write_or_check(path, text, check):
    """Writes text; with check writes nothing and says whether the file holds it, whatever its line ends."""
    if not check:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        return True
    try:
        with open(path, encoding="utf-8", newline=None) as f:
            return f.read() == text
    except OSError:
        return False


def run_scan(root, check, send_path, recv_path, tally_path):
    rows, tally, files = scan(root, op_names(send_path), op_names(recv_path))
    print(f"files {files['all']}, unique {files['unique']}, duplicates {files['all'] - files['unique']}; "
          f"GMS: map {files['map']} ({files['map_unique']} unique), all {files['gms']} ({files['gms_unique']} unique)")
    same = True
    for path, text in ((tally_path, tally_text(tally)), (os.path.join(root, "index.tsv"), index_text(rows))):
        if not write_or_check(path, text, check):
            print(f"{path}: would change", file=sys.stderr)
            same = False
    return 0 if same else 1


def run_expup(path):
    capture = read_msb(path)
    columns = ("gained", "message", "total", "rest", "parameter", "additional")
    print("\t".join(("seconds", "command") + columns))
    for record in capture.records:
        if record.op == EXP_UP and not record.outbound:
            packet = decode_exp_up(record.payload)
            seconds = (record.ticks - capture.records[0].ticks) / 10_000_000
            print("\t".join([f"{seconds:.3f}", str(packet["command"])]
                            + [str(packet.get(column, "")) for column in columns]))
    return 0


def synthetic(version, build, records):
    """A capture's bytes. Its addresses are from the documentation range, so a test can tell if they leak."""
    def text(value):
        return bytes([len(value)]) + value.encode("ascii")
    data = struct.pack("<H", version) + text("203.0.113.7") + struct.pack("<H", 1)
    data += text("203.0.113.8") + struct.pack("<H", 2) + struct.pack("<BI", 0, build)
    for ticks, op, outbound, payload in records:
        data += RECORD.pack(ticks, len(payload), op, outbound) + payload + bytes(EXTRA_BYTES.get(version, 0))
    return data


def selftest():
    # Golden: an ExpUp of "quest - turn in with loots.msb" (message 1010, quest) and one of "blackmarket_1.msb".
    add = bytes.fromhex("00a0b2220000000000f203799fb1b80100000000000000000000000000000000")
    assert decode_exp_up(add) == {"command": 0, "gained": 2273952, "message": 1010, "total": 7393615737, "rest": 0,
                                  "parameter": 0, "additional": False}
    assert decode_exp_up(bytes.fromhex("01add8fd0a00000000")) == {"command": 1, "rest": 184408237}
    for bad in (b"", b"\x00", add[:-1], b"\x02" + add[1:], add + b"\x00"):
        try:
            decode_exp_up(bad)
        except ValueError:
            continue
        raise AssertionError(f"decoded {bad!r}")

    # 0x2027 carries 8 more bytes per record, 0x2030 none; a read with the wrong rule does not reach the end.
    enter = b"\x00" + struct.pack("<i", 2000336) + bytes(38)
    tick = 630000000000000000
    records = [(tick, REQUEST_FIELD_ENTER, 0, enter), (tick + 1, 0x0006, 0, b""), (tick + 2, 0x0020, 1, b"\x01\x02"),
               (tick + 3, 0x010D, 0, b"\x03"), (tick + 4, 0x00B1, 0, b""), (tick + 5, REQUEST_FIELD_ENTER, 0, b"\x05")]
    good = synthetic(0x2027, GMS_BUILD, records)
    capture = parse_msb(good)
    assert (capture.version, capture.build, len(capture.records)) == (0x2027, GMS_BUILD, 6)
    assert capture.records[2] == Record(tick + 2, 0x0020, True, b"\x01\x02")
    newer = synthetic(0x2030, 2550, records)
    capture = parse_msb(newer)
    assert (capture.version, capture.build, len(capture.records)) == (0x2030, 2550, 6)
    assert capture.records[3].payload == b"\x03"
    assert field_maps(capture.records) == [2000336], "an error reply names no map"
    for bad in (good[:-1], good[:20], b"", struct.pack("<H", 0x2031) + good[2:], newer + bytes(3),
                struct.pack("<H", 0x2030) + good[2:], struct.pack("<H", 0x2027) + newer[2:]):
        try:
            parse_msb(bad)
        except ValueError:
            continue
        raise AssertionError(f"read a damaged capture of {len(bad)} bytes")

    with tempfile.TemporaryDirectory() as temp:
        ops = os.path.join(temp, "Op.cs")
        with open(ops, "w", encoding="utf-8") as f:
            f.write("enum Op {\n    Ping = 0x0006,\n    ItemDrop = 0x0029, // does not exist?\n"
                    "    //UNKNOWN = 0x00B1, // nullsub\n}\n")
        assert op_names(ops) == {0x0006: "Ping", 0x0029: "ItemDrop"}, "a line commented out names nothing"

        send = {0x0006: "Ping", 0x0016: "RequestFieldEnter"}
        recv = {0x0020: "Skill"}
        root = os.path.join(temp, "captures")
        files = {"loose/Quest/a.msb": good, "orion2/x/a-ABCDE.msb": good,
                 "inner/Part_1-81B5F.rar/b.msb": synthetic(0x2027, GMS_BUILD, records[1:2]),
                 "inner/Part_1-81B5F.rar/c.msb": good, "loose/Mini Games/k.msb": newer,
                 "doll/k2.msb": synthetic(0x2027, 2504, records), "inner/Part_1-81B5F.rar/notes.txt": b"not a capture"}
        for rel, data in files.items():
            os.makedirs(os.path.dirname(os.path.join(root, rel)), exist_ok=True)
            with open(os.path.join(root, rel), "wb") as f:
                f.write(data)
        rows, tally, count = scan(root, send, recv)
        # The map's files count a byte copy twice, inner/ counts only in all_files and unique_files, KMS files nowhere.
        assert dict(tally) == {("S", "RequestFieldEnter"): [4, 6, 2], ("S", "Ping"): [2, 4, 2], ("C", "Skill"): [2, 3, 1],
                               ("S", "0x010D"): [2, 3, 1], ("S", "0x00B1"): [2, 3, 1]}, dict(tally)
        assert (count["all"], count["unique"], count["gms"], count["map"]) == (6, 4, 4, 2), count
        assert (count["gms_unique"], count["map_unique"]) == (2, 1), count
        by_file = {row[0]: dict(zip(INDEX_HEADER, row)) for row in rows}
        assert [row[0] for row in rows] == ["doll/k2.msb", "loose/Mini Games/k.msb", "loose/Quest/a.msb",
                                            "orion2/x/a-ABCDE.msb", "inner/Part_1-81B5F.rar/b.msb",
                                            "inner/Part_1-81B5F.rar/c.msb"], "the map's files come first"
        kept, copy = by_file["loose/Quest/a.msb"], by_file["orion2/x/a-ABCDE.msb"]
        assert kept["duplicate_of"] == "" and copy["duplicate_of"] == "loose/Quest/a.msb"
        assert by_file["inner/Part_1-81B5F.rar/c.msb"]["duplicate_of"] == "loose/Quest/a.msb"
        assert (kept["system"], copy["system"], by_file["inner/Part_1-81B5F.rar/b.msb"]["system"]) == ("Quest", "a", "Part_1")
        assert by_file["doll/k2.msb"]["system"] == "k2"
        assert kept["maps"] == "2000336" and kept["start"] == stamp(tick), kept
        assert kept["server_ops"] == "RequestFieldEnter:2 0x00B1:1 0x010D:1 Ping:1" and kept["client_ops"] == "Skill:1"
        kms = by_file["loose/Mini Games/k.msb"]
        assert kms["maps"] == "" and "Ping" not in kms["server_ops"] and "0x0006:1" in kms["server_ops"], kms
        tally_tsv = tally_text(tally)
        assert tally_tsv.splitlines()[0].split("\t") == list(TALLY_HEADER)
        assert "S\tPing\t2\t4\t2" in tally_tsv.splitlines()
        assert ".msb" not in tally_tsv
        for text in (tally_tsv, index_text(rows)):
            assert "203.0.113" not in text, "an address left a capture"

        path = os.path.join(temp, "tally.tsv")
        assert not write_or_check(path, tally_tsv, True), "a missing file would change"
        assert not os.path.exists(path), "a check writes nothing"
        assert write_or_check(path, tally_tsv, False) and write_or_check(path, tally_tsv, True)
        with open(path, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(tally_tsv)
        assert write_or_check(path, tally_tsv, True), "a checkout's CRLF is no change"
        assert not write_or_check(path, tally_tsv + "x", True)

        with open(os.path.join(root, "loose", "damaged.msb"), "wb") as f:
            f.write(good[:-1])
        try:
            scan(root, send, recv)
        except ValueError as error:
            assert "loose/damaged.msb" in str(error), error
        else:
            raise AssertionError("a damaged capture was counted")
    print("selftest ok")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", nargs="?", choices=("scan", "expup"))
    parser.add_argument("path", nargs="?", help="scan: the capture folder; expup: a .msb file")
    parser.add_argument("--check", action="store_true", help="scan: write nothing, exit 1 if either file would change")
    parser.add_argument("--selftest", action="store_true", help="check the rules on a synthetic capture")
    args = parser.parse_args()
    if args.selftest:
        return selftest()
    if not args.command or not args.path:
        parser.error("name a command and a path")
    try:
        if args.command == "scan":
            return run_scan(args.path, args.check, os.path.join(OPS, "SendOp.cs"), os.path.join(OPS, "RecvOp.cs"), TALLY)
        return run_expup(args.path)
    except (OSError, OverflowError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
