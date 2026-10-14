#!/bin/bash
# Linux: puts the launcher on your PATH and in your application menu, then installs the game servers from the
# latest release of MapleStory2-Offline (or from a Setup.exe / zip you already downloaded: ./install.sh --from FILE).
#
#   ./install.sh [--from <MapleStory2-Offline-Setup.exe|zip>] [--home <program folder>]
#
# Nothing goes outside your home folder: ~/.local/bin/ms2offline, ~/.local/share/applications/ms2offline.desktop
# and the program folder (default ~/Games/MapleStory2-Offline).
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOME_DIR="${MS2OFFLINE_HOME:-$HOME/Games/MapleStory2-Offline}"
FROM=""
while [ $# -gt 0 ]; do
    case "$1" in
        --from) FROM="$2"; shift 2 ;;
        --home) HOME_DIR="$2"; shift 2 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

for tool in python3 umu-run ss; do
    command -v "$tool" >/dev/null || { echo "missing: $tool (umu-run comes with the umu-launcher package)" >&2; exit 1; }
done

mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME_DIR"
chmod +x "$HERE/ms2offline.py"
ln -sfn "$HERE/ms2offline.py" "$HOME/.local/bin/ms2offline"

# The project's icon (Launcher/app.ico) as a PNG for the menu entry and notifications.
ICON="$HERE/../Launcher/app.ico"
[ -f "$ICON" ] || ICON="$HERE/app.ico"
if [ ! -f "$HOME_DIR/icon.png" ] && [ -f "$ICON" ]; then
    python3 - "$ICON" "$HOME_DIR/icon.png" <<'EOF' || cp "$ICON" "$HOME_DIR/icon.ico"
import sys
from PIL import Image
icon = Image.open(sys.argv[1])
icon.size = max(icon.info.get("sizes", {icon.size}))
icon.convert("RGBA").save(sys.argv[2])
EOF
fi

sed -e "s|@HOME@|$HOME_DIR|g" -e "s|@BIN@|$HOME/.local/bin/ms2offline|g" -e "s|@DIR@|$HERE|g" "$HERE/ms2offline.desktop" > "$HOME/.local/share/applications/ms2offline.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true

[ -f "$HOME_DIR/launcher.ini" ] || cp "$HERE/launcher.ini.example" "$HOME_DIR/launcher.ini"

if [ -n "$FROM" ]; then
    MS2OFFLINE_HOME="$HOME_DIR" "$HERE/ms2offline.py" install --from "$FROM"
elif [ ! -d "$HOME_DIR/Maple2/Maple2.Server.World" ]; then
    MS2OFFLINE_HOME="$HOME_DIR" "$HERE/ms2offline.py" install
fi

echo
echo "Installed. Program folder: $HOME_DIR"
echo "  ms2offline status     what is installed and whether the game was found"
echo "  ms2offline play       play (also in your application menu: MapleStory 2 Offline)"
echo "  ms2offline stop       stop servers left running"
