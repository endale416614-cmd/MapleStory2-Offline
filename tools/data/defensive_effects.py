#!/usr/bin/env python3
"""DefensiveProperty damage-taken rates of additional effects (DL-03), for the server's defensive-effects.json (the ingest drops them). Input: a folder made by `M2dPack extract <Xml.m2d> additionaleffect/ <dir>`."""

import json
import os
import tempfile
import xml.etree.ElementTree as ET


def collect(effect_dir):
    """For every *.xml under effect_dir (recursive; id = the file name, an int), each level element's BasicProperty@level and
    DefensiveProperty@papDamageR/@mapDamageR; keeps a level when a rate is not 0 and beginCondition@applyPvPZoneType is not 2
    (the PvP-only copies), the first occurrence per (id, level) (a level appears once per feature, the copies agree).
    Returns ({id: {level: [physical, magic]}}, files that do not parse)."""
    effects = {}
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
                rates = [float(defensive.get('papDamageR', '0') or 0), float(defensive.get('mapDamageR', '0') or 0)]
                if rates == [0.0, 0.0]:
                    continue
                effects.setdefault(effect_id, {}).setdefault(int(basic.get('level', '1')), rates)
    return effects, bad_files


def selftest():
    def level(lv, pvp, pap, mag):
        return (f'<level feature="CharacterBalance_01"><beginCondition level="1" applyPvPZoneType="{pvp}" />'
                f'<BasicProperty level="{lv}" /><DefensiveProperty papDamageR="{pap}" mapDamageR="{mag}" /></level>')

    files = {
        '10100151.xml': f'<ms2>{level(1, 1, 0.25, 0.25)}{level(2, 1, 0.3, 0.3)}{level(1, 1, 0.9, 0.9)}</ms2>',
        '10100152.xml': f'<ms2>{level(1, 2, 0.125, 0.125)}</ms2>',
        '10100061.xml': f'<ms2><level><BasicProperty level="1" /><DefensiveProperty papDamageR="0.1" mapDamageR="0.1" /></level></ms2>',
        '10000000.xml': f'<ms2>{level(1, 1, 0, 0)}</ms2>',
        '99999999.xml': '<ms2>',
    }
    with tempfile.TemporaryDirectory() as tmpdir:
        for name, text in files.items():
            with open(os.path.join(tmpdir, name), 'w', encoding='utf-8') as f:
                f.write(text)
        effects, bad = collect(tmpdir)
    expected = {10100151: {1: [0.25, 0.25], 2: [0.3, 0.3]}, 10100061: {1: [0.1, 0.1]}}
    if effects != expected or bad != 1:
        print(f"selftest failed: {effects}, {bad} bad files")
        return False
    print("selftest ok")
    return True


def main():
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--effect-dir', required=True)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()

    effects, bad = collect(args.effect_dir)
    data = {"Effects": {str(effect_id): {str(lv): effects[effect_id][lv] for lv in sorted(effects[effect_id])}
                        for effect_id in sorted(effects)}}
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, indent=1, ensure_ascii=False, separators=(',', ': '))
        f.write('\n')
    print(f"{len(effects)} effects, {sum(len(levels) for levels in effects.values())} levels, {bad} files not parsed")


if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == '--selftest':
        sys.exit(0 if selftest() else 1)
    main()
