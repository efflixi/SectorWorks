#!/usr/bin/env python3
"""Separate 441-row expanded-NTFS preparation and exact Windows-output checking."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("ordinary_handoff", Path(__file__).with_name("writer-handoff.py"))
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)

SAVE_METHOD = "NtfsExpandedWorkflowTests.EveryCandidateDiskStagesAndSavesWithIndependentChecks"
COMMIT_METHOD = "NtfsExpandedWorkflowTests.EveryCandidateDiskDirectlyCommitsAfterValidatedSaveAs"


def rows():
    features = ["ordinary", *[f"{residency}-{feature}" for residency in ("resident", "nonresident")
                              for feature in ("moved-data", "split-data", "moved-index")]]
    result = []
    for version in ("1-2", "3-0", "3-1"):
        for feature in features:
            for container in ("raw", "vhd-fixed", "vhd-dynamic", "vhdx-fixed", "vhdx-dynamic"):
                for layout in ("filesystem", "mbr", "gpt"):
                    for physical in ((512, 4096) if container.startswith("vhdx") else (512,)):
                        result.append(f"{container}.{layout}.ntfs{version}-{feature}.p{physical}.short")
    return sorted(result)


def source_files():
    result = {item["file"]: item for item in base.source_files()}
    for directory in ("src", "tests", "tools"):
        for path in (base.REPO / directory).rglob("*"):
            relative = path.relative_to(base.REPO)
            if path.is_file() and not {"bin", "obj"}.intersection(relative.parts) and (
                    path.suffix in (".axaml", ".patch") or path.name.endswith(".json")):
                result[relative.as_posix()] = {"file": relative.as_posix(), "sha256": base.digest(path)}
    for path in (base.REPO / "scripts").iterdir():
        if path.suffix in (".py", ".ps1", ".sh"):
            relative = path.relative_to(base.REPO).as_posix()
            result[relative] = {"file": relative, "sha256": base.digest(path)}
    return [result[name] for name in sorted(result)]


def prepare(root, no_build=False):
    base.require(sys.platform.startswith("linux"), "Linux authoring and independent checks are required")
    base.require(not root.exists() and not root.is_symlink(), "Preparation root must be fresh")
    base.require(not any(key.startswith(("SECTORWORKS_WRITER_", "SECTORWORKS_EXPANDED_")) for key in os.environ),
                 "Clear existing handoff variables before preparation")
    snapshot = source_files()
    tools = base.tools()
    root.mkdir(parents=True)
    inputs = root / "inputs"
    inputs.mkdir()
    results = root / "linux-preparation"
    results.mkdir()
    report = {"schema": 2, "status": "pending", "sourceFiles": snapshot, "nativeTools": tools}
    try:
        command = [base.dotnet(), "test", base.PROJECT, "--no-restore", "-c", "Release",
                   "--filter", "FullyQualifiedName~" + SAVE_METHOD,
                   "--logger", "trx;LogFileName=linux-inputs.trx", "--results-directory", str(results)]
        if no_build:
            command.insert(3, "--no-build")
            report["prebuiltTestAssemblySha256"] = base.digest(
                base.REPO / "tests/SectorWorks.StorageTests/bin/Release/net10.0/SectorWorks.StorageTests.dll")
        report["command"] = command
        environment = dict(os.environ, SECTORWORKS_EXPANDED_EXPORT=str(inputs))
        with (results / "dotnet.log").open("xb") as log:
            child = subprocess.Popen(command, cwd=base.REPO, env=environment, stdout=log,
                                     stderr=subprocess.STDOUT, start_new_session=True)
            try:
                report["exitCode"] = child.wait(timeout=2400)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait(timeout=5)
                raise TimeoutError("Owned expanded preparation process group exceeded 40 minutes")
        base.require(report["exitCode"] == 0, "Expanded Linux preparation failed; retain every attempt")
        base.trx(results / "linux-inputs.trx", 441)
        base.require(source_files() == snapshot, "Source changed during preparation; start a fresh stable run")
        entries = []
        for row in rows():
            item = base.load(base.child(inputs, row + ".json"))
            base.require(item["row"] == row and item["file"] == row + ".disk", "Export row identity mismatch")
            base.verify_file(base.child(inputs, item["file"]), item["length"], item["sha256"])
            entries.append(item)
        base.require(len(list(inputs.glob("*.json"))) == 441 and len(list(inputs.glob("*.disk"))) == 441,
                     "Unexpected or missing expanded exports")
        catalog = {"schema": 2, "status": "linux-expanded-inputs-verified", "inputs": entries,
                   "sourceFiles": snapshot, "nativeTools": tools, "linuxTrxSha256": base.digest(results / "linux-inputs.trx")}
        base.write_new(inputs / "catalog.json", catalog)
        catalog_sha = base.digest(inputs / "catalog.json")
        (root / "catalog.sha256").write_text(catalog_sha + "\n", encoding="ascii")
        report.update(status="passed", rowCount=441, catalogSha256=catalog_sha)
        print("441 expanded Linux authoring/managed/native workflows passed; catalog SHA256 " + catalog_sha)
    except Exception as error:
        report.update(status="failed", error=str(error))
        raise
    finally:
        base.write_new(root / "preparation.json", report)


def validate_catalog(inputs):
    path = base.child(inputs, "catalog.json")
    catalog = base.load(path)
    sha = base.digest(path)
    preparation = base.load(inputs.parent / "preparation.json")
    base.require(preparation["status"] == "passed" and preparation["catalogSha256"] == sha
                 and (inputs.parent / "catalog.sha256").read_text().strip() == sha,
                 "Original successful Linux preparation and separate seal are required")
    trx = inputs.parent / "linux-preparation/linux-inputs.trx"
    base.require(base.digest(trx) == catalog["linuxTrxSha256"], "Preparation TRX changed")
    base.trx(trx, 441)
    entries = {item["row"]: item for item in catalog["inputs"]}
    base.require(catalog["schema"] == 2 and catalog["status"] == "linux-expanded-inputs-verified"
                 and len(catalog["inputs"]) == len(entries) == 441 and sorted(entries) == rows(),
                 "Incomplete, duplicate or unqualified expanded catalog")
    for row, item in entries.items():
        base.require(item["file"] == row + ".disk", "Input identity mismatch")
        base.verify_file(base.child(inputs, item["file"]), item["length"], item["sha256"])
    return sha, entries


def native_check(entry, output, scratch, tools, log):
    container, layout, _, _, _ = entry["row"].split(".")
    raw = output
    if container != "raw":
        raw = scratch / "converted.raw"
        base.run([tools["qemu-img"]["path"], "convert", "-q", "-f",
                  "vhdx" if container.startswith("vhdx") else "vpc", "-O", "raw", str(output), str(raw)], log)
    offset, length = base.volume_bounds(raw, layout)
    base.require((offset, length) == (entry["volumeOffset"], entry["volumeLength"]), "Independent partition bounds differ")
    volume = scratch / "volume.img"
    with raw.open("rb") as source, volume.open("xb") as target:
        source.seek(offset)
        remaining = length
        while remaining:
            chunk = source.read(min(65536, remaining))
            base.require(bool(chunk), "Truncated selected volume")
            target.write(chunk)
            remaining -= len(chunk)
    tool = lambda name, *args: base.run([tools[name]["path"], *map(str, args)], log)
    tool("ntfsfix", "-n", volume)
    tool("ntfsinfo", "-m", volume)
    tool("ntfsinfo", "-F", "/Renamed \u03bb directory/Renamed.txt", volume)
    tool("ntfscluster", "-F", "/Legacy/NOTE.TXT", volume)
    base.require(tool("ntfscat", volume, "/Legacy/NOTE.TXT") == bytes(i % 251 for i in range(19000)), "Grown nonresident data differs")
    for path in ("/LARGE.BIN", "/Renamed \u03bb directory/Renamed.txt"):
        base.require(tool("ntfscat", volume, path) == b"candidate shared overlay\n", "Resident/shared data differs")


def verify(inputs, outputs, path, workflow):
    base.require(sys.platform.startswith("linux") and not path.exists(), "New Linux verification report required")
    report = {"schema": 2, "status": "pending", "workflow": workflow, "outputs": []}
    try:
        sha, entries = validate_catalog(inputs)
        tools = base.tools()
        windows = base.load(outputs / "windows-run.json")
        base.require(windows["schema"] == 2 and windows["status"] == "pending-independent-linux-validation" and windows["exitCode"] == 0
                     and windows["platform"] == "Win32NT" and windows["catalogSha256"] == sha
                     and windows["workflow"] == workflow, "Windows run failed or has different provenance")
        trx = outputs / "windows.trx"
        base.require(base.digest(trx) == windows["trxSha256"], "Windows TRX changed")
        base.trx(trx, 441)
        expected = {row + "." + workflow for row in rows()}
        base.require({p.stem for p in outputs.glob("*.json")} - {"windows-run"} == expected,
                     "Missing or unexpected output queues")
        base.require({p.stem for p in outputs.glob("*.disk")} == expected, "Missing or unexpected exact outputs")
        report.update(catalogSha256=sha, windowsTrxSha256=base.digest(trx), nativeTools=tools)
        scratch_root = path.parent / (path.stem + "-work")
        scratch_root.mkdir()
        for row in rows():
            result = {"row": row, "status": "pending", "commands": []}
            report["outputs"].append(result)
            entry = base.load(outputs / (row + "." + workflow + ".json"))
            base.require(entry["schema"] == 1 and entry["status"] == "pending-independent-linux-validation"
                         and entry["row"] == row and entry["workflow"] == workflow
                         and entry["file"] == row + "." + workflow + ".disk" and entry["catalogSha256"] == sha
                         and entry["inputSha256"] == entries[row]["sha256"] and "Windows" in entry["platform"],
                         "Queue source/input/output identity mismatch")
            output = base.child(outputs, entry["file"])
            base.verify_file(output, entry["length"], entry["sha256"])
            scratch = scratch_root / row
            scratch.mkdir()
            native_check(entry, output, scratch, tools, result["commands"])
            base.verify_file(output, entry["length"], entry["sha256"])
            result.update(status="passed", sha256=entry["sha256"], length=entry["length"], inputSha256=entry["inputSha256"])
            shutil.rmtree(scratch)
        scratch_root.rmdir()
        validate_catalog(inputs)
        report["status"] = "passed"
        print(f"441 exact Windows {workflow} outputs independently verified; original inputs unchanged; no skips.")
    except Exception as error:
        report.update(status="failed", error=str(error))
        raise
    finally:
        base.write_new(path, report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("--root", type=Path, required=True)
    prep.add_argument("--no-build", action="store_true")
    check = commands.add_parser("verify")
    check.add_argument("--inputs", type=Path, required=True)
    check.add_argument("--outputs", type=Path, required=True)
    check.add_argument("--report", type=Path, required=True)
    check.add_argument("--workflow", choices=("saveas", "commit"), required=True)
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.root.absolute(), args.no_build)
    else:
        verify(args.inputs.absolute(), args.outputs.absolute(), args.report.absolute(), args.workflow)


if __name__ == "__main__":
    main()
