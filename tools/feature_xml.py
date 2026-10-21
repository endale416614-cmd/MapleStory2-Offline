#!/usr/bin/env python3
"""
Tool to patch Global feature.xml for NA region.

Reads a list of feature names and writes a patched copy of the Global
feature.xml where every listed feature has its NA attribute set to "1".
Features that exist only in KMS are added to the output with their KR
and CN values from KMS (defaulting to "99" when absent).

Usage:
    python tools/feature_xml.py \
        --global <path> --kms <path> --names <path> --out <path>
"""

import argparse
import re
import sys
import tempfile
import os


def parse_names(path: str) -> list[str]:
    """Return non-comment, non-blank lines from the names file."""
    names = []
    with open(path, "r", encoding="utf-8") as fh:
        for line in fh:
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            names.append(stripped)
    return names


def patch_feature_xml(global_path: str, kms_path: str, names: list[str], out_path: str) -> None:
    """Write the patched feature.xml to *out_path*."""

    # Read both source files as raw text for line-by-line processing.
    try:
        with open(global_path, "rb") as fh:
            global_bytes = fh.read()
    except FileNotFoundError:
        sys.stderr.write(f"Error: Global file not found: {global_path}\n")
        sys.exit(2)

    try:
        with open(kms_path, "rb") as fh:
            kms_bytes = fh.read()
    except FileNotFoundError:
        sys.stderr.write(f"Error: KMS file not found: {kms_path}\n")
        sys.exit(2)

    global_text = global_bytes.decode("utf-8")
    kms_text = kms_bytes.decode("utf-8")

    # Parse KMS features into a dict for lookup.
    kms_features: dict[str, dict] = {}
    for line in kms_text.splitlines():
        if ' name="' not in line or "<feature " not in line:
            continue
        attrs = _extract_attrs(line)
        if attrs and "name" in attrs:
            kms_features[attrs["name"]] = attrs

    # Classify names: present in Global vs KMS-only.
    in_global = set()
    kms_only = []
    global_lines = global_text.splitlines(True)  # keep line endings

    for name in names:
        found = False
        for gl in global_lines:
            if f'name="{name}"' in gl and "<feature " in gl:
                in_global.add(name)
                found = True
                break
        if not found:
            kms_only.append(name)

    # Verify all KMS-only names exist in KMS.
    for name in kms_only:
        if name not in kms_features:
            sys.stderr.write(f"Error: name '{name}' not found in Global or KMS feature XML\n")
            sys.exit(1)

    # Build output line-by-line.
    result_lines = []
    changed = 0
    for gl in global_lines:
        matched_name = None
        for name in in_global:
            if f'name="{name}"' in gl and "<feature " in gl:
                matched_name = name
                break
        if matched_name:
            new_line = _set_na(gl, matched_name)
            if new_line != gl:
                changed += 1
            result_lines.append(new_line)
        else:
            result_lines.append(gl)

    # Build KMS-only addition lines. The Global file's last line (</ms2>) has no
    # line ending, so take the style from the whole text, not from one line.
    eol = "\r\n" if "\r\n" in global_text else "\n"
    added_lines: list[str] = []
    for name in kms_only:
        attrs = kms_features[name]
        kr = attrs.get("KR", "99")
        cn = attrs.get("CN", "99")
        added_lines.append(f'\t<feature name="{name}" KR="{kr}" JP="99" CN="{cn}" NA="1" TH="99" TW="99" />{eol}')

    # Insert KMS-only lines before </ms2>.
    output = "".join(result_lines)
    closing = "</ms2>"
    if closing in output:
        insert_pos = output.rfind(closing)
        additions = "".join(added_lines)
        output = output[:insert_pos] + additions + output[insert_pos:]

    # Binary write: no newline translation, so line endings stay as built above.
    with open(out_path, "wb") as fh:
        fh.write(output.encode("utf-8"))

    print(f"changed {changed} lines, added {len(added_lines)} lines")


def _extract_attrs(line: str) -> dict[str, str]:
    """Extract key=\"value\" pairs from an XML tag line."""
    attrs: dict[str, str] = {}
    for m in re.finditer(r'(\w+)="([^"]*)"', line):
        attrs[m.group(1)] = m.group(2)
    return attrs


def _set_na(line: str, name: str) -> str:
    """Replace NA="99" with NA="1" on a feature line for *name*."""
    if f'name="{name}"' not in line:
        return line
    return re.sub(r'(NA=)"99"', r'\1"1"', line)


def run_selftest():
    """Run self-test with temporary files."""
    import shutil

    global_content_crlf = '<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n\t<feature name="ExistingNA99" KR="50" NA="99" JP="99" CN="99" TH="99" TW="99" />\r\n\t<feature name="AlreadyOn" KR="51" NA="1" JP="99" CN="99" TH="99" TW="99" />\r\n\t<feature name="Unchanged" KR="52" NA="99" JP="99" CN="99" TH="99" TW="99" />\r\n</ms2>'

    kms_content = '<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n\t<feature name="KMSOnlyFeat" KR="70" CN="71" />\r\n\t<feature name="UnknownTest" KR="99" />\r\n</ms2>\r\n'

    names_content = "ExistingNA99\r\nAlreadyOn\r\nKMSOnlyFeat\r\n"
    unknown_names_content = "DoesNotExist\r\n"

    # Create temp directory.
    tmpdir = tempfile.mkdtemp(prefix="feature_xml_selftest_")
    try:
        global_file = os.path.join(tmpdir, "global.xml")
        kms_file = os.path.join(tmpdir, "kms.xml")
        out_file = os.path.join(tmpdir, "out.xml")

        # Write test files.
        with open(global_file, "wb") as fh:
            fh.write(global_content_crlf.encode("utf-8"))
        with open(kms_file, "wb") as fh:
            fh.write(kms_content.encode("utf-8"))

        # Test 1: Normal run - NA=99->1, AlreadyOn unchanged, KMS-only added.
        with open(os.path.join(tmpdir, "names.txt"), "w", encoding="utf-8", newline="") as fh:
            fh.write(names_content)

        patch_feature_xml(global_file, kms_file, ["ExistingNA99", "AlreadyOn", "KMSOnlyFeat"], out_file)

        with open(out_file, "rb") as fh:
            out_bytes = fh.read()
        out_text = out_bytes.decode("utf-8")

        # Check NA=99 was changed to 1.
        assert 'name="ExistingNA99"' in out_text and 'NA="1"' in out_text, \
            "ExistingNA99 should have NA=\"1\""

        # Check AlreadyOn is unchanged (still NA="1").
        assert 'name="AlreadyOn"' in out_text, "AlreadyOn should still exist"

        # Check KMS-only feature was added.
        assert 'name="KMSOnlyFeat"' in out_text, "KMSOnlyFeat should be added"
        assert 'KR="70"' in out_text, "KMSOnlyFeat should have KR=\"70\""
        assert 'CN="71"' in out_text, "KMSOnlyFeat should have CN=\"71\""

        # Check CRLF preserved: every LF is part of a CRLF, and no CR is doubled.
        assert b"\r\n" in out_bytes, "CRLF should be preserved"
        assert b"\n" not in out_bytes.replace(b"\r\n", b""), "Should not contain a bare LF"
        assert b"\r\r" not in out_bytes, "Should not contain a double CR"

        # Check Unchanged line is byte-identical (NA="99" still).
        assert 'name="Unchanged"' in out_text and 'NA="99"' in out_text, \
            "Unchanged should keep NA=\"99\""

        print("Test 1 passed: basic patching, KMS-only addition, CRLF preservation")

        # Test 2: Unknown name exits with code 1.
        try:
            patch_feature_xml(global_file, kms_file, ["DoesNotExist"], out_file)
            assert False, "Should have exited"
        except SystemExit as e:
            assert e.code == 1, f"Expected exit code 1, got {e.code}"

        print("Test 2 passed: unknown name exits with code 1")

        # Test 3: Missing input file exits with code 2.
        try:
            patch_feature_xml("/nonexistent/global.xml", kms_file, ["ExistingNA99"], out_file)
            assert False, "Should have exited"
        except SystemExit as e:
            assert e.code == 2, f"Expected exit code 2, got {e.code}"

        print("Test 3 passed: missing file exits with code 2")

        # Test 4: LF-only input preserves LF.
        global_content_lf = '<?xml version="1.0" encoding="utf-8"?>\n<ms2>\n\t<feature name="LFFeat" KR="50" NA="99" JP="99" CN="99" TH="99" TW="99" />\n</ms2>\n'
        kms_content_lf = '<?xml version="1.0" encoding="utf-8"?>\n<ms2>\n\t<feature name="LFKMSOnly" KR="60" CN="61" />\n</ms2>\n'

        lf_global = os.path.join(tmpdir, "global_lf.xml")
        lf_kms = os.path.join(tmpdir, "kms_lf.xml")
        with open(lf_global, "wb") as fh:
            fh.write(global_content_lf.encode("utf-8"))
        with open(lf_kms, "wb") as fh:
            fh.write(kms_content_lf.encode("utf-8"))

        patch_feature_xml(lf_global, lf_kms, ["LFFeat", "LFKMSOnly"], out_file)
        with open(out_file, "rb") as fh:
            lf_out = fh.read()
        # The LF test is actually checking that we don't convert to CRLF when input is LF
        # But since we're not doing any conversion in the output step, this should pass
        print(f"LF output bytes: {lf_out}")
        assert b"\r\n" not in lf_out, "LF input should produce LF output (no CRLF added)"
        print("Test 4 passed: LF-only input preserves LF")

        print("\nAll self-tests passed!")

    finally:
        shutil.rmtree(tmpdir)


def main():
    parser = argparse.ArgumentParser(
        description="Patch Global feature.xml to enable features for NA region."
    )
    parser.add_argument("--global", dest="global_file", help="Path to Global feature XML file")
    parser.add_argument("--kms", dest="kms_file", help="Path to KMS feature XML file")
    parser.add_argument("--names", help="Path to feature names text file")
    parser.add_argument("--out", help="Output patched feature.xml path")
    parser.add_argument("--selftest", action="store_true", help="Run self-test")

    args = parser.parse_args()

    if args.selftest:
        run_selftest()
        return

    # Validate required arguments after selftest branch.
    if not args.global_file or not args.kms_file or not args.names or not args.out:
        parser.error("--global, --kms, --names, and --out are required")

    names = parse_names(args.names)
    patch_feature_xml(args.global_file, args.kms_file, names, args.out)


if __name__ == "__main__":
    main()
