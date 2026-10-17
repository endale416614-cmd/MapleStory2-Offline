# Developer tools

None of these tools is shipped to players.

- `ClientDiff`: read-only comparison of `Xml.m2d` archives from different clients, plus extraction of chosen path prefixes.
- `KmsSplit`: converts the consolidated KMS XML into per-id NA-style XML, and verifies, validates and self-tests that conversion.
- `M2dPack`: writes `.m2d`/`.m2h` archives by overlaying a folder of files on a base archive, and verifies, inspects and self-tests the result.
- `package_release.py`: builds the player release folder, installer, full zip and update zip.
- `sign_update.py`: signs an update zip with the update signing key.
