#!/usr/bin/env python3
"""Import only the approved NTFS sources and its minimal Core-internal helpers.

Run from the repository root only to recreate the namespace-only baseline.
Local repairs are deliberately separate; rerunning overwrites those repairs.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / "artifacts/ntfs-security-investigation/upstream"
DEST = Path(__file__).resolve().parent
COMMIT = "59d7cadab839c6d8dfcf52f8be5efe6d2ced190f"
HELPERS = ["ObjectCache.cs", "Utilities.cs"]


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    entries = []
    sources = [
        (path, Path("Imported") / path.relative_to(UPSTREAM / "Library/DiscUtils.Ntfs"))
        for path in sorted((UPSTREAM / "Library/DiscUtils.Ntfs").rglob("*.cs"))
    ]
    sources += [
        (UPSTREAM / "Library/DiscUtils.Core/Internal" / name, Path("Internal") / name)
        for name in HELPERS
    ]
    for source, relative in sources:
        raw = source.read_bytes()
        text = raw.decode("utf-8-sig").replace("\r\n", "\n")
        text = text.replace("DiscUtils.Ntfs", "SectorWorks.ManagedNtfs")
        text = text.replace("DiscUtils.Internal", "SectorWorks.ManagedNtfs.Internal")
        text = "using DiscUtils;\n" + text
        output = text.encode()
        target = DEST / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(output)
        entries.append({
            "input": str(source.relative_to(UPSTREAM)),
            "inputSha256": sha256(raw),
            "output": str(relative),
            "namespaceBaselineSha256": sha256(output),
        })
    license_bytes = (UPSTREAM / "LICENSE.txt").read_bytes()
    (DEST / "LICENSE.upstream.txt").write_bytes(license_bytes)
    manifest = {
        "repository": "https://github.com/DiscUtils/DiscUtils",
        "commit": COMMIT,
        "licenseSha256": sha256(license_bytes),
        "transformations": [
            "UTF-8 BOM removal and CRLF to LF",
            "DiscUtils.Ntfs -> SectorWorks.ManagedNtfs",
            "DiscUtils.Internal -> SectorWorks.ManagedNtfs.Internal",
            "prepend using DiscUtils for public signed Core API",
            "Local surgical repairs follow this reproducible baseline; see repair-provenance.json",
        ],
        "files": entries,
    }
    (DEST / "upstream-inputs.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
