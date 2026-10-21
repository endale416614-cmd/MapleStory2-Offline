#!/usr/bin/env python3
"""
Tool to analyze feature tables from Global and KMS sources.

This tool compares feature names between Global and KMS feature tables,
identifying features that are enabled in KMS but not in Global (kms-only)
or features that are enabled in KMS but disabled in Global for NA locale (na-off).
"""

import argparse
import sys
import xml.etree.ElementTree as ET
from typing import List, Set


def parse_feature_xml(file_path: str) -> List[dict]:
    """Parse feature XML file and return list of feature dictionaries."""
    try:
        tree = ET.parse(file_path)
        root = tree.getroot()
        
        features = []
        for feature in root.findall('feature'):
            name = feature.get('name')
            kr = feature.get('KR', '0')
            na = feature.get('NA', '0')
            jp = feature.get('JP', '0')
            cn = feature.get('CN', '0')
            tw = feature.get('TW', '0')
            
            features.append({
                'name': name,
                'KR': kr,
                'NA': na,
                'JP': jp,
                'CN': cn,
                'TW': tw
            })
        return features
    except ET.ParseError as e:
        sys.stderr.write(f"Error parsing XML file {file_path}: {e}\n")
        sys.exit(2)
    except Exception as e:
        sys.stderr.write(f"Error reading file {file_path}: {e}\n")
        sys.exit(2)


def get_kms_only_features(global_features: List[dict], kms_features: List[dict]) -> Set[str]:
    """Get features that are enabled in KMS but not present in Global."""
    global_names = {f['name'] for f in global_features}
    kms_only = set()
    
    for feature in kms_features:
        if feature['KR'] != '99' and feature['name'] not in global_names:
            kms_only.add(feature['name'])
    
    return kms_only


def get_na_off_features(global_features: List[dict], kms_features: List[dict]) -> Set[str]:
    """Get features that are enabled in KMS but disabled in Global for NA locale."""
    global_dict = {f['name']: f for f in global_features}
    na_off = set()
    
    for feature in kms_features:
        if feature['KR'] != '99':
            global_feature = global_dict.get(feature['name'])
            if global_feature and (global_feature.get('NA', '0') == '99' or 'NA' not in global_feature):
                na_off.add(feature['name'])
    
    return na_off


def run_selftest():
    """Run self-test with sample data."""
    # Test data for self-test
    global_test_content = '''<?xml version="1.0" encoding="utf-8"?>
<ms2>
  <feature name="test1" KR="1" NA="1" JP="1" CN="1" TH="1" TW="99" />
  <feature name="test2" KR="1" NA="99" JP="1" CN="1" TH="1" TW="99" />
</ms2>'''
    
    kms_test_content = '''<?xml version="1.0" encoding="utf-8"?>
<ms2>
  <feature name="test1" KR="1" NA="1" JP="1" CN="1" TH="1" TW="99" />
  <feature name="test2" KR="1" NA="1" JP="1" CN="1" TH="1" TW="99" />
  <feature name="test3" KR="1" NA="1" JP="1" CN="1" TH="1" TW="99" />
  <feature name="test4" KR="99" NA="1" JP="1" CN="1" TH="1" TW="99" />
</ms2>'''
    
    import tempfile
    import os
    
    # Create temporary files
    with tempfile.NamedTemporaryFile(mode='w', suffix='.xml', delete=False) as f:
        f.write(global_test_content)
        global_file = f.name
    
    with tempfile.NamedTemporaryFile(mode='w', suffix='.xml', delete=False) as f:
        f.write(kms_test_content)
        kms_file = f.name
    
    try:
        # Test kms-only mode
        global_features = parse_feature_xml(global_file)
        kms_features = parse_feature_xml(kms_file)
        
        kms_only = get_kms_only_features(global_features, kms_features)
        assert 'test3' in kms_only, "test3 should be in kms-only list"
        assert 'test4' not in kms_only, "test4 should not be in kms-only list"
        
        # Test na-off mode
        na_off = get_na_off_features(global_features, kms_features)
        assert 'test2' in na_off, "test2 should be in na-off list"
        assert 'test3' not in na_off, "test3 should not be in na-off list"
        
        print("selftest passed")
    finally:
        os.unlink(global_file)
        os.unlink(kms_file)


def main():
    parser = argparse.ArgumentParser(description='Analyze feature tables from Global and KMS sources')
    parser.add_argument('--global', dest='global_file', help='Path to Global feature XML file')
    parser.add_argument('--kms', dest='kms_file', help='Path to KMS feature XML file')
    parser.add_argument('--mode', choices=['kms-only', 'na-off'], default='kms-only',
                        help='Analysis mode (default: kms-only)')
    parser.add_argument('--out', help='Output file path')
    parser.add_argument('--selftest', action='store_true', help='Run self-test')
    
    args = parser.parse_args()
    
    if args.selftest:
        run_selftest()
        return
    
    # Validate required arguments after selftest branch
    if not args.global_file or not args.kms_file:
        parser.error("--global and --kms are required")
    
    # Process files
    global_features = parse_feature_xml(args.global_file)
    kms_features = parse_feature_xml(args.kms_file)
    
    if args.mode == 'kms-only':
        result = get_kms_only_features(global_features, kms_features)
        mode_name = 'kms-only'
    else:  # na-off
        result = get_na_off_features(global_features, kms_features)
        mode_name = 'na-off'
    
    sorted_result = sorted(result)
    
    output_text = f"# kms_features.py --mode {mode_name}: {len(sorted_result)} names\n"
    output_text += "\n".join(sorted_result) + "\n"
    
    if args.out:
        with open(args.out, 'w', encoding='utf-8') as f:
            f.write(output_text)
    else:
        print(output_text, end='')


if __name__ == '__main__':
    main()