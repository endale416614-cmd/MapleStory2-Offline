#!/usr/bin/env python3
"""DefensiveProperty damage-taken rates (DL-03) and invincible values (NB-21) of additional effects, for the server's defensive-effects.json (the ingest drops the rates and keeps invincible only as a flag). Input: a folder made by `M2dPack extract <Xml.m2d> additionaleffect/ <dir>`, from the NA client's Xml.m2d (the nightly's gives the same file). --check writes nothing and exits 1 if --out would change, whatever its line ends."""

import json
import os
import sys
import tempfile
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # for msb: python -I does not add the script's folder
from msb import write_or_check


def collect(effect_dir):
    """For every *.xml under effect_dir (recursive; id = the file name, an int), each level element's BasicProperty@level and
    DefensiveProperty@papDamageR/@mapDamageR; keeps a level when a rate is not 0 and beginCondition@applyPvPZoneType is not 2
    (the PvP-only copies), the first occurrence per (id, level) (a level appears once per feature, the copies agree).
    DefensiveProperty@invincible the same way, when it is not 0 (NB-21).
    Returns ({id: {level: [physical, magic]}}, {id: {level: invincible}}, files that do not parse)."""
    effects = {}
    invincible = {}
    bad_files = 0
    for root, _, files in os.walk(effect_dir):
        for file in files:
            if not file.endswith('.xml'):
                continue
            try:
                effect_id = int(os.path.splitext(file)[0])
                tree = ET.parse(os.path.join(root, file))
            except (ValueError, ET.ParseError):
                bad_files += 1
                continue
            for level in tree.getroot().iter('level'):
                basic = level.find('BasicProperty')
                defensive = level.find('DefensiveProperty')
                if basic is None or defensive is None:
                    continue
                begin = level.find('beginCondition')
                if begin is not None and begin.get('applyPvPZoneType', '0') == '2':
                    continue
                value = int(defensive.get('invincible', '0') or 0)
                if value:
                    invincible.setdefault(effect_id, {}).setdefault(int(basic.get('level', '1')), value)
                rates = [float(defensive.get('papDamageR', '0') or 0), float(defensive.get('mapDamageR', '0') or 0)]
                if rates == [0.0, 0.0]:
                    continue
                effects.setdefault(effect_id, {}).setdefault(int(basic.get('level', '1')), rates)
    return effects, invincible, bad_files


def selftest():
    def level(lv, pvp, pap, mag, inv=0):
        return (f'<level feature="CharacterBalance_01"><beginCondition level="1" applyPvPZoneType="{pvp}" />'
                f'<BasicProperty level="{lv}" /><DefensiveProperty papDamageR="{pap}" mapDamageR="{mag}" invincible="{inv}" /></level>')

    files = {
        '10100151.xml': f'<ms2>{level(1, 1, 0.25, 0.25)}{level(2, 1, 0.3, 0.3)}{level(1, 1, 0.9, 0.9)}</ms2>',
        '10100152.xml': f'<ms2>{level(1, 2, 0.125, 0.125, 1)}</ms2>',
        '10100061.xml': f'<ms2><level><BasicProperty level="1" /><DefensiveProperty papDamageR="0.1" mapDamageR="0.1" /></level></ms2>',
        '10000000.xml': f'<ms2>{level(1, 1, 0, 0, 2)}{level(1, 1, 0, 0, 1)}</ms2>',
        '99999999.xml': '<ms2>',
    }
    with tempfile.TemporaryDirectory() as tmpdir:
        for name, text in files.items():
            with open(os.path.join(tmpdir, name), 'w', encoding='utf-8') as f:
                f.write(text)
        effects, invincible, bad = collect(tmpdir)
        out = os.path.join(tmpdir, 'out.json')
        args = ['--effect-dir', tmpdir, '--out', out]
        # A missing file would change and a check writes nothing; a checkout's CRLF is no change; an edit is.
        results = [main(args + ['--check']), os.path.exists(out), main(args), main(args + ['--check'])]
        with open(out, encoding='utf-8') as f:
            written = f.read()
        with open(out, 'w', encoding='utf-8', newline='\r\n') as f:
            f.write(written)
        results.append(main(args + ['--check']))
        with open(out, 'a', encoding='utf-8') as f:
            f.write(' ')
        results.append(main(args + ['--check']))
    expected = {10100151: {1: [0.25, 0.25], 2: [0.3, 0.3]}, 10100061: {1: [0.1, 0.1]}}
    if effects != expected or invincible != {10000000: {1: 2}} or bad != 1:
        print(f"selftest failed: {effects}, {invincible}, {bad} bad files")
        return False
    if results != [1, False, 0, 0, 0, 1]:
        print(f"selftest failed: --check gave {results}")
        return False
    print("selftest ok")
    return True


def main(argv=None):
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--effect-dir', required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--check', action='store_true', help='write nothing, exit 1 if --out would change')
    args = parser.parse_args(argv)

    effects, invincible, bad = collect(args.effect_dir)
    data = {"Effects": {str(effect_id): {str(lv): effects[effect_id][lv] for lv in sorted(effects[effect_id])}
                        for effect_id in sorted(effects)},
            "Invincible": {str(effect_id): {str(lv): invincible[effect_id][lv] for lv in sorted(invincible[effect_id])}
                           for effect_id in sorted(invincible)}}
    print(f"{len(effects)} effects, {sum(len(levels) for levels in effects.values())} levels, "
          f"{sum(len(levels) for levels in invincible.values())} invincible levels, {bad} files not parsed")
    if not write_or_check(args.out, json.dumps(data, indent=1, ensure_ascii=False, separators=(',', ': ')) + '\n', args.check):
        print(f"{args.out}: would change", file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--selftest':
        sys.exit(0 if selftest() else 1)
    sys.exit(main())
