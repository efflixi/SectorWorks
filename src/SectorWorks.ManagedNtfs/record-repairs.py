#!/usr/bin/env python3
"""Record or verify the exact local repair delta against the approved import."""
import argparse
import difflib
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
UPSTREAM = ROOT / "artifacts/ntfs-security-investigation/upstream"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    inputs = json.loads((HERE / "upstream-inputs.json").read_text())
    outputs = []
    patches = []
    for entry in inputs["files"]:
        raw = (UPSTREAM / entry["input"]).read_bytes()
        if sha256(raw) != entry["inputSha256"]:
            raise SystemExit("Approved input hash mismatch: " + entry["input"])
        baseline = raw.decode("utf-8-sig").replace("\r\n", "\n")
        baseline = baseline.replace("DiscUtils.Ntfs", "SectorWorks.ManagedNtfs")
        baseline = baseline.replace("DiscUtils.Internal", "SectorWorks.ManagedNtfs.Internal")
        baseline = "using DiscUtils;\n" + baseline
        if sha256(baseline.encode()) != entry["namespaceBaselineSha256"]:
            raise SystemExit("Namespace baseline hash mismatch: " + entry["input"])
        target = HERE / entry["output"]
        output = target.read_bytes()
        delta = difflib.unified_diff(
            baseline.splitlines(keepends=True),
            output.decode().splitlines(keepends=True),
            fromfile="namespace-baseline/" + entry["output"],
            tofile="repaired/" + entry["output"],
        )
        for line in delta:
            patches.append(line if line.endswith("\n") else line + "\n\\ No newline at end of file\n")
        outputs.append({
            "path": entry["output"],
            "sha256": sha256(output),
            "locallyRepaired": output != baseline.encode(),
        })
    patch = "".join(patches)
    manifest = {
        "approvedCommit": inputs["commit"],
        "inputManifestSha256": sha256((HERE / "upstream-inputs.json").read_bytes()),
        "repairPatch": "upstream-repairs.patch",
        "repairPatchSha256": sha256(patch.encode()),
        "excludedFromCompilation": ["Imported/NtfsFormatter.cs", "Imported/Quotas.cs"],
        "signedDependencies": {"DiscUtils.Core": "0.16.13", "DiscUtils.Streams": "0.16.13"},
        "supplementarySources": [
            {"path": str(path.relative_to(HERE)), "sha256": sha256(path.read_bytes())}
            for path in sorted(HERE.glob("*.cs"))
        ],
        "outputs": outputs,
    }
    data = json.dumps(manifest, indent=2) + "\n"
    for name, content in [("repair-provenance.json", data), ("upstream-repairs.patch", patch)]:
        path = HERE / name
        if args.verify:
            if path.read_text() != content:
                raise SystemExit("Stale repair provenance: " + name)
        else:
            path.write_text(content)
    print("Repair provenance verified." if args.verify else "Repair provenance recorded.")


if __name__ == "__main__":
    main()
