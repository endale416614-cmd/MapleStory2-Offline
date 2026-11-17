#!/usr/bin/env python3
"""FinalStatusProperty stat rates and values of additional effects (PI-09, BT-264), and the effects that apply only in PvP zones (applyPvPZoneType 2, BT-266), for the server's final-status-effects.json (the ingest drops both). Input: a folder made by `M2dPack extract <Xml.m2d> additionaleffect/ <dir>`, from the NA client's Xml.m2d (the nightly's gives the same file). --check writes nothing and exits 1 if --out would change, whatever its line ends."""

import json
import os
import sys
import tempfile
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # for msb: python -I does not add the script's folder
from msb import write_or_check

# The client's stat codes of FinalStatusProperty/Stat (final<code>rate, final<code>value) and the server's BasicAttribute names.
STATS = {'hp': 'Health', 'sp': 'Spirit', 'asp': 'AttackSpeed', 'msp': 'MovementSpeed', 'atp': 'Accuracy', 'jmp': 'JumpHeight',
         'pap': 'PhysicalAtk', 'map': 'MagicalAtk', 'wapmin': 'MinWeaponAtk', 'wapmax': 'MaxWeaponAtk'}


def collect(effect_dir):
    """For every *.xml under effect_dir (recursive; id = the file name, an int), each level element's BasicProperty@level and
    FinalStatusProperty/Stat final<code>rate / final<code>value; keeps a level when one is not 0, the first occurrence per
    (id, level) (a level appears once per feature, the copies agree). Also the levels whose beginCondition@applyPvPZoneType is 2.
    Returns ({id: {level: {'Rates': {name: rate}, 'Values': {name: value}}}}, {id: [levels]}, files that do not parse)."""
    effects = {}
    pvp_only = {}
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
                if basic is None:
                    continue
                lv = int(basic.get('level', '1'))
                begin = level.find('beginCondition')
                if begin is not None and begin.get('applyPvPZoneType', '0') == '2' and lv not in pvp_only.setdefault(effect_id, []):
                    pvp_only[effect_id].append(lv)
                stat = level.find('FinalStatusProperty/Stat')
                if stat is None:
                    continue
                entry = {}
                for kind, suffix in (('Rates', 'rate'), ('Values', 'value')):
                    found = {name: float(stat.get(f'final{code}{suffix}', '0') or 0) for code, name in STATS.items()}
                    found = {name: v for name, v in found.items() if v != 0}
                    if found:
                        entry[kind] = found
                if entry:
                    effects.setdefault(effect_id, {}).setdefault(lv, entry)
    return effects, pvp_only, bad_files


def selftest():
    def level(lv, pvp, stat):
        return (f'<level><beginCondition level="1" applyPvPZoneType="{pvp}" /><BasicProperty level="{lv}" />'
                f'<FinalStatusProperty><Stat {stat} /></FinalStatusProperty></level>')

    q = '"'
    files = {
        '70000027.xml': '<ms2>' + level(1, 0, 'finalhprate=' + q + '11.5' + q) + level(1, 0, 'finalhprate=' + q + '1' + q) + '</ms2>',
        '70002183.xml': '<ms2>' + level(1, 0, 'finalpapvalue="1" finalpaprate="0.25" finalspvalue="-30"') + level(2, 0, '') + '</ms2>',
        '10200052.xml': '<ms2>' + level(1, 2, '') + level(2, 2, '') + '</ms2>',
        '10200051.xml': '<ms2>' + level(1, 1, '') + '</ms2>',
        '99999999.xml': '<ms2>',
    }
    with tempfile.TemporaryDirectory() as tmpdir:
        for name, text in files.items():
            with open(os.path.join(tmpdir, name), 'w', encoding='utf-8') as f:
                f.write(text)
        effects, pvp_only, bad = collect(tmpdir)
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
    expected = {70000027: {1: {'Rates': {'Health': 11.5}}},
                70002183: {1: {'Rates': {'PhysicalAtk': 0.25}, 'Values': {'PhysicalAtk': 1.0, 'Spirit': -30.0}}}}
    if effects != expected or pvp_only != {10200052: [1, 2]} or bad != 1:
        print(f"selftest failed: {effects}, {pvp_only}, {bad} bad files")
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

    effects, pvp_only, bad = collect(args.effect_dir)
    data = {"Effects": {str(effect_id): {str(lv): effects[effect_id][lv] for lv in sorted(effects[effect_id])}
                        for effect_id in sorted(effects)},
            "PvpOnly": {str(effect_id): sorted(pvp_only[effect_id]) for effect_id in sorted(pvp_only)}}
    print(f"{len(effects)} effects, {sum(len(levels) for levels in effects.values())} levels, "
          f"{len(pvp_only)} PvP-only effects, {bad} files not parsed")
    if not write_or_check(args.out, json.dumps(data, indent=1, ensure_ascii=False, separators=(',', ': ')) + '\n', args.check):
        print(f"{args.out}: would change", file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--selftest':
        sys.exit(0 if selftest() else 1)
    sys.exit(main())
