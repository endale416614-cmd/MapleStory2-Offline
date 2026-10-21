"""Fill the client's missing names in a string file (string/en/itemname.xml, npcname.xml) from the server's merged
string file of the same name (KMS-04): a client key without a name gets the server's name; an id the client lacks gets
the server's whole key line, added before </ms2>. A Korean (Hangul) client name is replaced by the server's name when
that has no Hangul (KMS-04b); any other name the client has is never changed. Line-based: every other line stays
byte-identical. Usage: fill_client_names.py <client file> <server file> --out <file> | --selftest"""
import re
import sys

KEY = re.compile(r'^(\s*)<key id="(\d+)"(.*?)\s*/>\s*$')
NAME = re.compile(r"""\sname=(?:"([^"]*)"|'([^']*)')""")  # either quote: three KMS NPC lines use name='...'
HANGUL = re.compile(r"[ᄀ-ᇿ㄰-㆏가-힣]")


def keys(lines):
    """id -> (line index, name or None, the name attribute as written or None) for every key line."""
    result = {}
    for i, line in enumerate(lines):
        match = KEY.match(line)
        if match:
            name = NAME.search(match.group(3))
            value = None if not name else name.group(1) if name.group(1) is not None else name.group(2)
            result[int(match.group(2))] = (i, value, name.group(0) if name else None)
    return result


def fill(client_text, server_text):
    """(new client text, ids whose name was filled, ids added, ids whose Korean name was replaced). Line ends of the
    client text are kept; a new or replaced name is the server's name attribute as the server file writes it."""
    lines = client_text.splitlines(True)
    server_lines = server_text.splitlines(True)
    client, server = keys(lines), keys(server_lines)
    filled, replaced = [], []
    for key_id, (index, name, attribute) in sorted(client.items()):
        _, server_name, server_attribute = server.get(key_id, (0, None, None))
        if not server_name:
            continue
        body = lines[index].rstrip("\r\n")
        end = lines[index][len(body):]
        if name is None:
            body = re.sub(r'\s*/>$', lambda _: server_attribute + " />", body)
            filled.append(key_id)
        elif name == "":
            body = body.replace(attribute, server_attribute, 1)  # an empty attribute: filled in place
            filled.append(key_id)
        elif HANGUL.search(name) and not HANGUL.search(server_name):
            body = body.replace(attribute, server_attribute, 1)
            replaced.append(key_id)
        else:
            continue
        lines[index] = body + end
    newline = "\r\n" if "\r\n" in client_text else "\n"
    added = [key_id for key_id, (index, name, _) in sorted(server.items()) if key_id not in client and name]
    close = next(i for i, line in enumerate(lines) if line.strip() == "</ms2>")
    lines[close:close] = [f"\t{server_lines[server[key_id][0]].strip()}{newline}" for key_id in added]
    return "".join(lines), filled, added, replaced


def selftest():
    import xml.etree.ElementTree as ET
    client = ('<?xml version="1.0" encoding="utf-8"?>\n<ms2>\n\t<key id="1" class="hat" />\n\t<key id="2" class="hat" name="Mine" />\n'
              '\t<key id="3" name="" class="hat" />\n\t<key id="6" name=\'비치 &quot;1&quot;\' feature="Event" />\n'
              '\t<key id="7" name=\'Mine &quot;2&quot;\' />\n\t<key id="8" name="한국" />\n</ms2>\n')
    server = ('<?xml version="1.0" encoding="utf-8"?>\n<ms2>\n\t<key id="1" class="hat" name="Cherub Halo" />\n'
              '\t<key id="2" class="hat" name="Theirs" />\n\t<key id="3" class="hat" name="Q &amp; A" />\n'
              '\t<key id="4" class="hat" name="New" />\n\t<key id="5" class="hat" name="" />\n'
              '\t<key id="6" name="Beachway &quot;1&quot;" />\n\t<key id="7" name="Other" />\n\t<key id="8" name="한국어" />\n</ms2>\n')
    text, filled, added, replaced = fill(client, server)
    assert filled == [1, 3] and added == [4] and replaced == [6], (filled, added, replaced)
    assert '\t<key id="1" class="hat" name="Cherub Halo" />' in text
    assert 'name="Mine"' in text and 'Theirs' not in text, "the client's own name stays"
    assert 'name="Q &amp; A"' in text and text.count('id="3"') == 1, "an empty client name is filled in place"
    assert '\t<key id="6" name="Beachway &quot;1&quot;" feature="Event" />' in text, "a Korean name becomes the English one"
    assert 'Other' not in text and '한국어' not in text, "an English name stays; a Korean server name replaces nothing"
    assert text.index('id="4"') < text.index('</ms2>') and 'id="5"' not in text
    ET.fromstring(text.split("\n", 1)[1])  # well-formed: no key with two name attributes
    assert fill(text, server)[1:] == ([], [], []), "a second run changes nothing"
    crlf = fill(client.replace("\n", "\r\n"), server)[0]
    assert "\n" not in crlf.replace("\r\n", ""), "CRLF line ends stay CRLF"
    print("selftest ok")


def main():
    if len(sys.argv) == 2 and sys.argv[1] == "--selftest":
        selftest()
        return
    if len(sys.argv) != 5 or sys.argv[3] != "--out":
        print("Usage: fill_client_names.py <client file> <server file> --out <file>")
        sys.exit(1)
    texts = []
    for path in sys.argv[1:3]:
        with open(path, encoding="utf-8", newline="") as f:  # newline="": keep the file's line ends
            texts.append(f.read())
        if not any(line.strip() == "<ms2>" for line in texts[-1].splitlines()):
            print(f"{path}: no <ms2> root line")
            sys.exit(1)
    text, filled, added, replaced = fill(*texts)
    with open(sys.argv[4], "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print(f"filled {len(filled)}, replaced {len(replaced)} Korean, added {len(added)}")


if __name__ == "__main__":
    main()
