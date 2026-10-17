# Security Policy

## Supported versions

Only the latest release receives fixes. Fixes are shipped as a new release or an update file on the
[Releases](https://github.com/endale416614-cmd/MapleStory2-Offline/releases) page.

## How the game is meant to be isolated

All servers listen on `127.0.0.1` only, and the client patch blocks every connection that does not go to this PC.
Updates are signed (ECDSA P-256): the launcher installs an update only when its signature matches the public key built
into the launcher, and every file matches the signed checksum list. The automatic update check only contacts
`api.github.com` and downloads only from GitHub over HTTPS.

Releases can also carry `release.json` and `release.json.sig`: the size and sha256 of every release file (Setup, full
zip, update zip), signed with the same key. From 1.1.9 the launcher's update check uses it when a release has it: it
refuses the release when the signature does not verify, when it is for another version, or when the downloaded update
file does not match the listed size and sha256. Releases without `release.json` are checked as before. The Setup and the
full zip are not yet checked automatically; compare them with `release.json` by hand (for example `certutil -hashfile
<file> SHA256`).

A way to reach the game servers from another computer, to make the client connect elsewhere, or to make the launcher
install an update that was not signed with the project's key or that writes outside the program folder counts as a
security problem.

## Reporting a vulnerability

Please report it privately through this repository's **Security** tab → **Report a vulnerability**, not as a public
issue. Include the build id (first lines of `summary.txt` in an Export Diagnostics file) and the steps to reproduce.
