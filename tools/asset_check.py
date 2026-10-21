#!/usr/bin/env python3
"""Static asset check for KMS-only items in a repacked client (task U-08, layer 1).

For every item id in the KMS id set but not in the Global one: does the icon exist in
Image, does every model asset (.nif) exist in its Model archive, and does every .dds
named inside those models exist (looked up by file name in Textures, Image and the model archives)?

Inputs are plain files; nothing is read from or written to the client.
  --listing DIR   <Label>.tsv from `ClientDiff <DIR> Label=<archive.m2d> ...` for
                  Image, Item, Character, Effect, Npc, Textures (path TAB size TAB sha256)
  --xml DIR       per-id item XML tree (item/<a>/<b>/<id>.xml), as in the repacked Xml.m2d
  --ids-gms/--ids-kms  JSON files with an "item" list
  --nif-root DIR  extracted models, one folder per archive label (Item/, Effect/, ...);
                  made with `ClientDiff --extract <Item.m2d> <DIR>/Item ""`
  --out DIR       receives u08-static.tsv and u08-static.md
"""
import argparse
import collections
import json
import os
import re
import xml.etree.ElementTree as ET

MODEL_LABELS = {"item": "Item", "character": "Character", "effect": "Effect", "npc": "Npc", "textures": "Textures"}
IMG_PREFIX = "data/resource/image/"
MODEL_PREFIX = "data/resource/model/"


def norm(p):
    p = p.replace("\\", "/").strip().lower()
    return p[2:] if p.startswith("./") else p


def load_listing(path):
    # lowercase path -> original-case path
    with open(path, encoding="utf-8") as f:
        return {norm(line.split("\t", 1)[0]): line.split("\t", 1)[0] for line in f if line.strip()}


def icon_path(prop):
    """Image-archive path the item declares, or None when it declares no file icon."""
    s = prop.get("slotIcon") or ""
    custom = prop.get("slotIconCustom") or ""
    if re.fullmatch(r"icon\d+\.png", s.strip(), re.I):  # relative default, real file is the custom one
        s = custom if "/" in custom else ""
    if not s.strip():
        return None
    n = norm(s)
    if n.startswith(IMG_PREFIX):
        return n[len(IMG_PREFIX):]
    if "/" in n:
        return n
    n = s.lower()  # bare name keeps its whitespace: a KMS file is named "11304865 .png"
    return "item/icon/" + (n if "." in n else n + ".png")  # bare name: game looks in item/icon


def nif_textures(data):
    """Names of .dds files inside a Gamebryo .nif (SizedString: uint32 length + chars)."""
    out = set()
    for m in re.finditer(rb"[ -~]{4,}", data):
        run = m.group()
        for dds in re.finditer(rb"[^\x00]*?\.dds", run, re.I):
            s0, e = m.start() + dds.start(), m.start() + dds.end()
            # the run may start with printable bytes of the length field: pick the start the length confirms
            for s in range(s0, e):
                if s >= 4 and int.from_bytes(data[s - 4:s], "little") == e - s:
                    break
            else:
                s = s0
            out.add(data[s:e].decode("ascii"))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--listing", required=True)
    ap.add_argument("--xml", required=True)
    ap.add_argument("--ids-gms", required=True)
    ap.add_argument("--ids-kms", required=True)
    ap.add_argument("--nif-root", required=True)
    ap.add_argument("--names", help="string/en/itemname.xml (default: <xml>/string/en/itemname.xml)")
    ap.add_argument("--test-list", help="text file; every '/item <id>' in it is shown in its own table")
    ap.add_argument("--notes", help="text appended verbatim to the md (spot-check record)")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    lst = {lab: load_listing(os.path.join(a.listing, lab + ".tsv")) for lab in ("Image", "Item", "Character", "Effect", "Npc", "Textures")}
    tex = {}  # lowercase basename -> found; textures live in Textures/<category>/ and, for effects, in Effect/textures/
    for lab in ("Textures", "Image", "Effect", "Item", "Character", "Npc"):
        for k in lst[lab]:
            tex[k.rsplit("/", 1)[-1]] = True

    gms = set(json.load(open(a.ids_gms, encoding="utf-8"))["item"])
    kms = set(json.load(open(a.ids_kms, encoding="utf-8"))["item"])
    ids = sorted(kms - gms)
    test_ids = []
    if a.test_list:
        test_ids = [int(x) for x in re.findall(r"/item (\d+)", open(a.test_list, encoding="utf-8").read())]

    xmlfiles = {}
    for root, _, fs in os.walk(a.xml):
        for f in fs:
            if re.fullmatch(r"\d+\.xml", f) and os.sep + "item" + os.sep in root + os.sep:
                xmlfiles[int(f[:-4])] = os.path.join(root, f)
    names = {}
    npath = a.names or os.path.join(a.xml, "string", "en", "itemname.xml")
    if os.path.exists(npath):
        for k in ET.parse(npath).getroot().iter("key"):
            names[int(k.get("id"))] = k.get("name")

    nif_cache = {}  # (label, lowercase rel) -> set of dds names, or None when the file is not on disk

    def scan(label, rel):
        key = (label, rel)
        if key not in nif_cache:
            p = os.path.join(a.nif_root, label, lst[label][rel])
            nif_cache[key] = nif_textures(open(p, "rb").read()) if os.path.isfile(p) else None
        return nif_cache[key]

    def check(i):
        r = dict(id=i, name=names.get(i, ""), icon="", model="", tex_ok=0, tex_missing=0, missing=[])
        if i not in xmlfiles:
            r.update(icon="no-xml", model="no-xml")
            return r
        root = ET.parse(xmlfiles[i]).getroot()
        prop = root.find(".//property")
        ip = icon_path(prop) if prop is not None else None
        if ip is None:
            r["icon"] = "n/a"
        elif ip in lst["Image"]:
            r["icon"] = "ok"
        else:
            r["icon"] = "missing"
            r["missing"].append("icon:" + ip)
        assets = [x.get("name") for x in root.findall(".//slots//asset") if (x.get("name") or "").strip()]
        files = [x for x in assets if not x.lower().startswith("urn:")]
        if not assets:
            r["model"] = "none-declared"
        elif not files:
            r["model"] = "urn"  # reference to an animation inside another model, no file to check
        else:
            bad, texnames = False, set()
            for x in files:
                n = norm(x)
                if n.startswith(MODEL_PREFIX):
                    n = n[len(MODEL_PREFIX):]
                lab, _, rel = n.partition("/")
                lab = MODEL_LABELS.get(lab)
                if lab is None or lab == "Textures":
                    bad = True
                    r["missing"].append("model(unlisted archive):" + x)
                elif rel not in lst[lab]:
                    bad = True
                    r["missing"].append(f"model:{lab}/{rel}")
                else:
                    t = scan(lab, rel)
                    if t is None:
                        r["missing"].append(f"nif-not-extracted:{lab}/{rel}")
                    else:
                        texnames |= t
            r["model"] = "missing" if bad else "ok"
            for t in sorted(texnames):
                if t.replace("\\", "/").rsplit("/", 1)[-1].lower() in tex:
                    r["tex_ok"] += 1
                else:
                    r["tex_missing"] += 1
                    r["missing"].append("tex:" + t)
        return r

    rows = [check(i) for i in ids]
    by_id = {r["id"]: r for r in rows}
    extra = [check(i) for i in test_ids if i not in by_id]
    by_id.update({r["id"]: r for r in extra})

    os.makedirs(a.out, exist_ok=True)
    with open(os.path.join(a.out, "u08-static.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("id\tname\ticon\tmodel\ttextures_ok\ttextures_missing\tmissing_paths\n")
        for r in rows:
            f.write(f"{r['id']}\t{r['name']}\t{r['icon']}\t{r['model']}\t{r['tex_ok']}\t{r['tex_missing']}\t{'; '.join(r['missing'])}\n")

    live = [r for r in rows if r["icon"] != "no-xml"]
    cnt = lambda p: sum(1 for r in live if p(r))
    icon_bad = lambda r: r["icon"] == "missing"
    model_bad = lambda r: r["model"] == "missing"
    tex_bad = lambda r: r["tex_missing"] > 0
    problem = lambda r: icon_bad(r) or model_bad(r) or tex_bad(r) or any(m.startswith("nif-not-extracted") for m in r["missing"])
    top = collections.Counter(m for r in live for m in set(r["missing"]))
    unextracted = sum(1 for v in nif_cache.values() if v is None)
    md = [
        "# U-08 static asset check (layer 1)", "",
        f"KMS-only item ids: {len(ids)}; {len(live)} have an XML in the repacked Xml.m2d, {len(ids) - len(live)} have none (empty KMS placeholders skipped by the converter, not in the client).",
        f"Distinct models scanned: {len(nif_cache) - unextracted}; not extracted/readable: {unextracted}.", "",
        "| result | items |", "|---|---|",
        f"| items fully ok (icon ok or no file icon, model ok or urn, all textures ok) | {cnt(lambda r: not problem(r) and r['model'] in ('ok', 'urn'))} |",
        f"| no model declared, icon ok or n/a | {cnt(lambda r: not problem(r) and r['model'] == 'none-declared')} |",
        f"| icon missing | {cnt(icon_bad)} |",
        f"| model missing | {cnt(model_bad)} |",
        f"| textures missing (at least one) | {cnt(tex_bad)} |",
        f"| no model declared (any icon state) | {cnt(lambda r: r['model'] == 'none-declared')} |",
        f"| model is a gamebryo-animation urn (not a file, not checkable) | {cnt(lambda r: r['model'] == 'urn')} |",
        f"| icon n/a (no file icon declared) | {cnt(lambda r: r['icon'] == 'n/a')} |", "",
    ]
    if test_ids:
        md += ["## Items on the TEST-U08 list", "", "| id | name | icon | model | tex ok/missing | missing |", "|---|---|---|---|---|---|"]
        for i in test_ids:
            r = by_id[i]
            kind = "" if i in set(ids) else " (not KMS-only)"
            md.append(f"| {i}{kind} | {r['name']} | {r['icon']} | {r['model']} | {r['tex_ok']}/{r['tex_missing']} | {'; '.join(r['missing'])} |")
        md.append("")
    md += ["## Top 20 missing paths (by items needing them)", "", "| items | path |", "|---|---|"]
    md += [f"| {n} | `{p}` |" for p, n in top.most_common(20)]
    md.append("")
    if a.notes:
        md += [open(a.notes, encoding="utf-8").read().rstrip(), ""]
    with open(os.path.join(a.out, "u08-static.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(md))
    print("\n".join(md[:18]))


if __name__ == "__main__":
    main()
