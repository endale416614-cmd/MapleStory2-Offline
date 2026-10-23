#!/usr/bin/env python3
"""Summon lists of NPCs (NB-09): aiInfo/@summonNpcIDs of every npc XML, for the server's summon-npcs.json (a summon node's npcId N is the Nth id, 1-based). Input: a folder of npc XMLs (M2dPack extract <Xml.m2d> npc/ <dir>)."""

import os
import xml.etree.ElementTree as ET
import json
import tempfile


def collect(npc_dir):
    """For every *.xml under npc_dir (recursive), the NPC id is the file name without extension (an int); parse with xml.etree.ElementTree (skip files that do not parse, counting them); the list is the first non-empty summonNpcIDs of any aiInfo element (split on commas, strip, ints); return {npc_id: [ids]} for the NPCs that have one."""
    result = {}
    bad_files = 0

    for root, _, files in os.walk(npc_dir):
        for file in files:
            if not file.endswith('.xml'):
                continue

            file_path = os.path.join(root, file)
            try:
                # Get NPC id from filename (without extension)
                npc_id = int(os.path.splitext(file)[0])

                # Parse XML
                tree = ET.parse(file_path)
                root_elem = tree.getroot()

                # Find aiInfo elements with summonNpcIDs
                for env in root_elem.findall('.//environment'):
                    for ai_info in env.findall('.//aiInfo'):
                        summon_ids = ai_info.get('summonNpcIDs', '').strip()
                        if summon_ids:
                            # Split and convert to integers
                            ids = [int(x.strip()) for x in summon_ids.split(',') if x.strip()]
                            if ids:
                                result[npc_id] = ids
                                break
            except Exception:
                bad_files += 1

    return result, bad_files


def collect_string_ids(npc_dir):
    """For every *.xml under npc_dir (recursive; id = file name without extension, an int), the first non-empty stringID attribute of any basic element (root.iter('basic')), stripped; returns {npc_id: string_id}; files that do not parse are skipped."""
    result = {}

    for root, _, files in os.walk(npc_dir):
        for file in files:
            if not file.endswith('.xml'):
                continue

            file_path = os.path.join(root, file)
            try:
                # Get NPC id from filename (without extension)
                npc_id = int(os.path.splitext(file)[0])

                # Parse XML
                tree = ET.parse(file_path)
                root_elem = tree.getroot()

                # Find basic elements with stringID attributes
                for basic in root_elem.iter('basic'):
                    string_id = basic.get('stringID', '').strip()
                    if string_id:
                        result[npc_id] = string_id
                        break  # Take the first non-empty stringID
            except Exception:
                # Skip files that don't parse
                pass

    return result


def collect_skill_probs(npc_dir):
    """For every *.xml under npc_dir (recursive; id = file name without extension, an int), 
    find the first skill element with a non-empty probs attribute. Returns {npc_id: [probs]} 
    for NPCs that have two or more probabilities."""
    result = {}

    for root, _, files in os.walk(npc_dir):
        for file in files:
            if not file.endswith('.xml'):
                continue

            file_path = os.path.join(root, file)
            try:
                # Get NPC id from filename (without extension)
                npc_id = int(os.path.splitext(file)[0])

                # Parse XML
                tree = ET.parse(file_path)
                root_elem = tree.getroot()

                # Find skill elements with probs attribute
                for skill in root_elem.iter('skill'):
                    probs_str = skill.get('probs', '')
                    if probs_str:
                        # Split and convert to integers
                        probs = [int(x.strip()) for x in probs_str.split(',') if x.strip()]
                        if len(probs) >= 2:
                            result[npc_id] = probs
                        break  # only the first skill element with a probs attribute
            except Exception:
                # Skip files that don't parse
                pass

    return result


def collect_races(npc_dir):
    """For every *.xml under npc_dir (recursive; id = file name without extension, an int),
    find the first basic element with a non-empty raceString attribute. Returns {npc_id: race_string}."""
    result = {}

    for root, _, files in os.walk(npc_dir):
        for file in files:
            if not file.endswith('.xml'):
                continue

            file_path = os.path.join(root, file)
            try:
                # Get NPC id from filename (without extension)
                npc_id = int(os.path.splitext(file)[0])

                # Parse XML
                tree = ET.parse(file_path)
                root_elem = tree.getroot()

                # Find basic elements with raceString attributes
                for basic in root_elem.iter('basic'):
                    race_string = basic.get('raceString', '').strip()
                    if race_string:
                        result[npc_id] = race_string
                        break  # Take the first non-empty raceString
            except Exception:
                # Skip files that don't parse
                pass

    return result


def collect_interact_skills(npc_dir):
    """For every *.xml under npc_dir (recursive; id = file name without extension, an int), the first interact element
    whose interactFunction is "UseSkill,<id>,<level>" (all 35 client NPCs with a function use it; others are skipped).
    Returns {npc_id: [skill_id, level, cool_time_ms]}: the server casts the skill when the NPC is used (BT-44)."""
    result = {}
    for root, _, files in os.walk(npc_dir):
        for file in files:
            if not file.endswith('.xml'):
                continue
            try:
                npc_id = int(os.path.splitext(file)[0])
                for interact in ET.parse(os.path.join(root, file)).getroot().iter('interact'):
                    parts = [part.strip() for part in interact.get('interactFunction', '').split(',')]
                    if len(parts) == 3 and parts[0] == 'UseSkill' and parts[1].isdigit() and parts[2].isdigit():
                        result[npc_id] = [int(parts[1]), int(parts[2]), int(interact.get('interactCoolTime') or 0)]
                        break
            except Exception:
                # Skip files that don't parse
                pass
    return result


def selftest():
    """Run self-test with temp folder."""
    with tempfile.TemporaryDirectory() as tmpdir:
        # Create test files
        npc_dir = os.path.join(tmpdir, 'npc')
        os.makedirs(npc_dir)

        # Test file 1: normal case
        xml1 = '''<ms2><environment><aiInfo path="a.xml" summonNpcIDs="23001002" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '23001001.xml'), 'w') as f:
            f.write(xml1)

        # Test file 2: multiple environments
        xml2 = '''<ms2><environment><aiInfo path="a.xml" summonNpcIDs="" /></environment><environment><aiInfo path="b.xml" summonNpcIDs="23407007, 23407008" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '23000072.xml'), 'w') as f:
            f.write(xml2)

        # Test file 3: empty aiInfo
        xml3 = '''<ms2><environment><aiInfo path="" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '11000001.xml'), 'w') as f:
            f.write(xml3)

        # Test file 4: broken XML
        with open(os.path.join(npc_dir, '99999999.xml'), 'w') as f:
            f.write('<ms2>')

        # Test file 5: stringID case (should be skipped in collect but included in collect_string_ids)
        xml5 = '''<ms2><environment><basic stringID="Gigantika_01" friendly="0" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '23001006.xml'), 'w') as f:
            f.write(xml5)

        # Test file 6: skill probs case (should be included in collect_skill_probs)
        xml6 = '''<ms2><environment><skill ids="40086001,40086011" levels="1,1" priorities="1,1" probs="50,50" coolDown="3000" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '21000101.xml'), 'w') as f:
            f.write(xml6)

        # Test file 7: one skill (should be excluded from collect_skill_probs)
        xml7 = '''<ms2><environment><skill ids="40086001" levels="1" priorities="1" probs="100" coolDown="3000" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '21000102.xml'), 'w') as f:
            f.write(xml7)

        # Test file 8: raceString case (should be included in collect_races)
        xml8 = '''<ms2><environment><basic raceString="animal" friendly="0" /></environment></ms2>'''
        with open(os.path.join(npc_dir, '21000013.xml'), 'w') as f:
            f.write(xml8)

        # Test files 9 and 10: a usable NPC (collect_interact_skills) and a function it skips
        with open(os.path.join(npc_dir, '21800911.xml'), 'w') as f:
            f.write('''<ms2><environment><interact interactFunction="UseSkill,50100511,2" interactCoolTime="7000" /></environment></ms2>''')
        with open(os.path.join(npc_dir, '21800999.xml'), 'w') as f:
            f.write('''<ms2><environment><interact interactFunction="UseSkill,x" interactCoolTime="7000" /></environment></ms2>''')

        result, bad = collect(npc_dir)
        string_ids = collect_string_ids(npc_dir)
        skill_probs = collect_skill_probs(npc_dir)
        races = collect_races(npc_dir)
        interact_skills = collect_interact_skills(npc_dir)

        expected = {23001001: [23001002], 23000072: [23407007, 23407008]}
        if result != expected:
            print(f"Self-test failed. Expected {expected}, got {result}")
            return False
        if bad != 1:
            print(f"Self-test failed. Expected 1 bad file, got {bad}")
            return False

        # Check string IDs test
        expected_string_ids = {23001006: "Gigantika_01"}
        if string_ids != expected_string_ids:
            print(f"String ID self-test failed. Expected {expected_string_ids}, got {string_ids}")
            return False

        # Check skill probs test
        expected_skill_probs = {21000101: [50, 50]}
        if skill_probs != expected_skill_probs:
            print(f"Skill probs self-test failed. Expected {expected_skill_probs}, got {skill_probs}")
            return False

        # Check races test
        expected_races = {21000013: "animal"}
        if races != expected_races:
            print(f"Races self-test failed. Expected {expected_races}, got {races}")
            return False

        expected_interact = {21800911: [50100511, 2, 7000]}
        if interact_skills != expected_interact:
            print(f"Interact skills self-test failed. Expected {expected_interact}, got {interact_skills}")
            return False

    print("selftest ok")
    return True


def main():
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--npc-dir', required=True)
    parser.add_argument('--out', required=True)

    args = parser.parse_args()

    result, bad = collect(args.npc_dir)
    string_ids = collect_string_ids(args.npc_dir)
    skill_probs = collect_skill_probs(args.npc_dir)
    races = collect_races(args.npc_dir)
    interact_skills = collect_interact_skills(args.npc_dir)

    # Write output
    # NPCs in id order; each list keeps the XML's order: a summon node's npcId N is the Nth entry.
    data = {"Summons": {str(npc_id): result[npc_id] for npc_id in sorted(result)}, 
            "StringIds": {str(npc_id): string_ids[npc_id] for npc_id in sorted(string_ids)},
            "SkillProbs": {str(npc_id): skill_probs[npc_id] for npc_id in sorted(skill_probs)},
            "Races": {str(npc_id): races[npc_id] for npc_id in sorted(races)},
            "InteractSkills": {str(npc_id): interact_skills[npc_id] for npc_id in sorted(interact_skills)}}

    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, indent=1, ensure_ascii=False, separators=(',', ': '))
        f.write('\n')

    print(f"{len(result)} NPCs with a summon list, {len(string_ids)} with a stringID, {len(skill_probs)} with skill probabilities, {len(races)} with race strings, {len(interact_skills)} usable (interact skills), {bad} files not parsed")


if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == '--selftest':
        if not selftest():
            sys.exit(1)
    else:
        main()