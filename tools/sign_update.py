r"""Signs an update zip in place: writes "MS2OFFLINE-SIG-1:<base64 DER>" into the zip comment, an ECDSA P-256 / SHA-256
signature over "MS2OFFLINE-UPDATE-1\n" + update.json + "\n" + files.json. files.json holds the sha256 of every file in the update, so
the signature covers all of it. The launcher refuses updates whose signature does not verify (Launcher/UpdateKey.cs).
The comment (not a file) keeps signed updates installable by launchers from before signing, which accept listed files only.

  python tools/sign_update.py <update.zip> [private key .pem]
  python tools/sign_update.py --manifest <release.json> [private key .pem]

--manifest writes <release.json>.sig: the base64 signature over "MS2OFFLINE-RELEASE-1\n" + release.json (size and sha256 of
every release file; Launcher/ReleaseManifest.cs). Publish both next to the release files.

The private key lives outside the repository (default: ..\Signing\update-signing-key.pem, or MS2OFFLINE_SIGNING_KEY).
"""
import base64
import os
import re
import sys
import zipfile

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_KEY = os.environ.get("MS2OFFLINE_SIGNING_KEY") or os.path.join(os.path.dirname(ROOT), "Signing", "update-signing-key.pem")
HEADER = b"MS2OFFLINE-UPDATE-1\n"
RELEASE_HEADER = b"MS2OFFLINE-RELEASE-1\n"
PREFIX = b"MS2OFFLINE-SIG-1:"


def launcher_public_key():
    with open(os.path.join(ROOT, "Launcher", "UpdateKey.cs"), encoding="utf-8") as f:
        encoded = re.search(r'PublicKey\s*=\s*"([^"]+)"', f.read()).group(1)
    return serialization.load_der_public_key(base64.b64decode(encoded))


def signature_for(message, key_path=None):
    key_path = key_path or DEFAULT_KEY
    if not os.path.exists(key_path):
        sys.exit(f"signing key not found: {key_path} (updates are never shipped unsigned)")
    with open(key_path, "rb") as f:
        key = serialization.load_pem_private_key(f.read(), password=None)
    signature = key.sign(message, ec.ECDSA(hashes.SHA256()))
    # Must verify with the key built into the launcher, or players would reject the file.
    launcher_public_key().verify(signature, message, ec.ECDSA(hashes.SHA256()))
    return signature


def sign(zip_path, key_path=None):
    with zipfile.ZipFile(zip_path) as z:
        if z.comment.startswith(PREFIX):
            sys.exit(f"{zip_path} is already signed")
        message = HEADER + z.read("update.json") + b"\n" + z.read("files.json")
    signature = signature_for(message, key_path)
    with zipfile.ZipFile(zip_path, "a") as z:
        z.comment = PREFIX + base64.b64encode(signature)
    print(f"signed {os.path.basename(zip_path)}")


def sign_manifest(manifest_path, key_path=None):
    with open(manifest_path, "rb") as f:
        message = RELEASE_HEADER + f.read()
    signature = signature_for(message, key_path)
    with open(manifest_path + ".sig", "wb") as f:
        f.write(base64.b64encode(signature) + b"\n")
    print(f"signed {os.path.basename(manifest_path)}")


if __name__ == "__main__":
    if sys.argv[1:2] == ["--manifest"]:
        sign_manifest(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
    else:
        sign(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
