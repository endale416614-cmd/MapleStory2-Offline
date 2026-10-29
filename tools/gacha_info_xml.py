#!/usr/bin/env python3
"""
Write the pack's table/gacha_info.xml: the Global file with the KMS capsule rows Global lacks.

Pack OpenGachaBox items name gacha ids only KMS has (e.g. 1001025); without the row the server cannot open the
capsule. Each KMS <randomBox> line whose randomBoxID is not in Global is added before </ms2>: rows for another
region's locale are skipped (KMS keeps a KR and a CN row for some ids), the locale attribute is removed so the NA
filter keeps the row, and each attribute Global writes on every row is added where the KMS row leaves it out, with
the value Global has where KMS leaves it out on the 50 ids both write (FILL; a dismantled capsule item pays
coinItemAmount coins). The Global part stays byte-identical.

Usage:
    py -3 -I tools/gacha_info_xml.py --global <Global table/gacha_info.xml> --kms <KMS table/gacha_info.xml> --out <path>
    py -3 -I tools/gacha_info_xml.py --selftest
"""

import argparse
import os
import re
import sys
import tempfile

ID = re.compile(r'\brandomBoxID="(\d+)"')
LOCALE = re.compile(r'\slocale="([^"]*)"')
FILL = (("randomBoxSeasonKR", ""), ("randomBoxSeasonCN", ""), ("randomBoxSeasonNA", ""), ("randomBoxSeasonJP", ""),
        ("seasonNumberOnIcon", "0"), ("coinItemAmount", "1"))


def merge(global_text: str, kms_text: str) -> tuple[str, int]:
    """Return the merged text and the number of added rows."""
    have = set(ID.findall(global_text))
    eol = "\r\n" if "\r\n" in global_text else "\n"
    added: list[str] = []
    for line in kms_text.splitlines():
        m = ID.search(line)
        if "<randomBox " not in line or not m or m.group(1) in have:
            continue
        locale = LOCALE.search(line)
        if locale and locale.group(1) not in ("", "KR"):
            continue
        row = LOCALE.sub("", line).strip()
        for name, value in FILL:
            if f" {name}=" not in row:
                row = re.sub(r"\s*/>$", f' {name}="{value}" />', row)
        have.add(m.group(1))
        added.append("\t" + row + eol)
    end = global_text.rfind("</ms2>")
    if end < 0:
        raise ValueError("Global file has no </ms2>")
    return global_text[:end] + "".join(added) + global_text[end:], len(added)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--global", dest="global_path")
    ap.add_argument("--kms")
    ap.add_argument("--out")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    if not (a.global_path and a.kms and a.out):
        ap.error("--global, --kms and --out are required")
    with open(a.global_path, "rb") as fh:
        global_text = fh.read().decode("utf-8")
    with open(a.kms, "rb") as fh:
        kms_text = fh.read().decode("utf-8-sig")
    text, n = merge(global_text, kms_text)
    with open(a.out, "wb") as fh:
        fh.write(text.encode("utf-8"))
    print(f"added {n} rows")
    return 0


def selftest() -> int:
    g = ('<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n'
         '\t<randomBox randomBoxID="1001001" individualDropBoxID="510000001" coinItemAmount="1" />\r\n</ms2>')
    k = ('<ms2>\r\n'
         '\t<randomBox randomBoxID="1001001" individualDropBoxID="999" />\r\n'
         '\t<randomBox randomBoxID="1001025" randomBoxGroupDesc="스킨" randomBoxSeasonKR="3" seasonNumberOnIcon="1" individualDropBoxID="510000025" coinItemID="22201025" />\r\n'
         '\t<randomBox randomBoxID="1004501" individualDropBoxID="545000001" locale="KR" />\r\n'
         '\t<randomBox randomBoxID="1004501" individualDropBoxID="545100001" locale="CN" />\r\n'
         '\t<randomBox randomBoxID="1004604" feature="23_02" individualDropBoxID="546000004" coinItemAmount="5" />\r\n'
         '</ms2>\r\n')
    text, n = merge(g, k)
    seasons = ' randomBoxSeasonCN="" randomBoxSeasonNA="" randomBoxSeasonJP=""'
    want = (g[:g.rfind("</ms2>")]
            + '\t<randomBox randomBoxID="1001025" randomBoxGroupDesc="스킨" randomBoxSeasonKR="3" seasonNumberOnIcon="1" individualDropBoxID="510000025" coinItemID="22201025"' + seasons + ' coinItemAmount="1" />\r\n'
            + '\t<randomBox randomBoxID="1004501" individualDropBoxID="545000001" randomBoxSeasonKR=""' + seasons + ' seasonNumberOnIcon="0" coinItemAmount="1" />\r\n'
            + '\t<randomBox randomBoxID="1004604" feature="23_02" individualDropBoxID="546000004" coinItemAmount="5" randomBoxSeasonKR=""' + seasons + ' seasonNumberOnIcon="0" />\r\n'
            + "</ms2>")
    checks = [
        ("Global rows kept byte-identical, its own ids never added again", text.startswith(g[:g.rfind("</ms2>")])),
        ("KMS-only rows added once, KR kept, CN skipped, locale removed, missing Global attributes filled, own values kept",
         text == want and n == 3),
    ]
    tmp = tempfile.mkdtemp(prefix="gacha_info_xml_")
    try:
        paths = [os.path.join(tmp, name) for name in ("g.xml", "k.xml", "out.xml")]
        for path, body, enc in ((paths[0], g, "utf-8"), (paths[1], k, "utf-8-sig")):
            with open(path, "wb") as fh:
                fh.write(body.encode(enc))
        main(["--global", paths[0], "--kms", paths[1], "--out", paths[2]])
        with open(paths[2], "rb") as fh:
            checks.append(("file mode: KMS byte-order mark read, output without one", fh.read() == want.encode("utf-8")))
    finally:
        for name in os.listdir(tmp):
            os.remove(os.path.join(tmp, name))
        os.rmdir(tmp)
    fail = 0
    for name, ok in checks:
        print(("ok   " if ok else "FAIL ") + name)
        fail += not ok
    print("selftest passed" if not fail else f"selftest FAILED ({fail})")
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
