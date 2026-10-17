#!/usr/bin/env python3
"""Linux authoring and independent verification of the bounded Windows writer matrix."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
import zlib

REPO = Path(__file__).resolve().parent.parent
MAX_IMAGE = 1024 * 1024 * 1024
PROJECT = "tests/SectorWorks.StorageTests/SectorWorks.StorageTests.csproj"
SAVE_FILTER = (
    "FullyQualifiedName~DiskStagedWorkflowTests.EveryQualifiedRowStagesReviewsAndValidatesNewOutput|"
    "FullyQualifiedName~NtfsStagedWorkflowTests.EveryQualifiedNtfsDiskStagesReviewsAndSavesWithoutChangingTheSource"
)
TOOLS = ("qemu-img", "fsck.fat", "mdir", "mtype", "ntfsfix", "ntfsinfo", "ntfscluster", "ntfscat")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def child(root, name):
    require(name not in ("", ".", "..") and not any(c in name for c in "/\\:"),
            "Invalid handoff filename")
    root = Path(root).absolute()
    for item in (root, *root.parents):
        require(not item.is_symlink(), "Linked handoff directories are forbidden")
    path = root / name
    require(not path.is_symlink() and not path.is_dir(), "Linked/directory handoff file")
    return path


def load(path):
    require(path.stat().st_size <= 2 * 1024 * 1024, "Oversized JSON evidence")
    return json.loads(path.read_text(encoding="utf-8"))


def write_new(path, value):
    with open(path, "x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, ensure_ascii=True)
        stream.write("\n")


def verify_file(path, length, sha):
    require(0 < length <= MAX_IMAGE and path.stat().st_size == length, "Image size mismatch")
    require(digest(path) == sha.upper(), "Image SHA256 mismatch: " + path.name)


def rows():
    result = []
    for container in ("raw", "vhd-fixed", "vhd-dynamic", "vhdx-fixed", "vhdx-dynamic"):
        for layout in ("filesystem", "mbr", "gpt"):
            for filesystem in ("fat12", "fat16", "fat32", "ntfs"):
                for physical in ((512, 4096) if container.startswith("vhdx") else (512,)):
                    result.append(f"{container}.{layout}.{filesystem}.p{physical}."
                                  + ("short" if filesystem == "ntfs" else "lfn"))
    return sorted(result)


def growth_rows():
    return [row for row in rows() if "-dynamic." in row and ".fat32." in row]


def trx(path, expected):
    tree = ET.parse(path)
    counters = tree.find(".//{*}Counters")
    results = tree.findall(".//{*}UnitTestResult")
    require(counters is not None and int(counters.attrib["total"]) == expected and
            int(counters.attrib["passed"]) == expected and len(results) == expected and
            all(item.attrib["outcome"] == "Passed" for item in results),
            "TRX must contain every expected case passed, with zero skips/failures")


def tools():
    result = {}
    for name in TOOLS:
        path = shutil.which(name) or shutil.which(name, path="/usr/sbin:/sbin")
        require(path is not None, "Missing mandatory independent tool: " + name)
        result[name] = {"path": path, "sha256": digest(Path(path))}
    return result


def dotnet():
    candidate = (os.environ.get("SECTORWORKS_DOTNET") or shutil.which("dotnet") or
                 str(Path.home() / ".local/share/SectorWorks/dotnet/dotnet"))
    require(Path(candidate).is_file() and os.access(candidate, os.X_OK),
            "Pinned SDK missing; use the repository's existing SECTORWORKS_DOTNET/user-local SDK convention")
    return candidate


def run(arguments, log, timeout=120):
    completed = subprocess.run(arguments, cwd=REPO, capture_output=True, timeout=timeout, check=False)
    log.append({"command": [str(a) for a in arguments], "exitCode": completed.returncode,
                "stdoutSha256": hashlib.sha256(completed.stdout).hexdigest().upper(),
                "stdout": None if Path(arguments[0]).name in ("ntfscat", "mtype") else completed.stdout.decode("utf-8", "replace"),
                "stderr": completed.stderr.decode("utf-8", "replace")})
    require(completed.returncode == 0, f"{Path(arguments[0]).name} failed with exit {completed.returncode}")
    return completed.stdout


def source_files():
    candidates = set()
    for directory in ("src", "tests", "tools"):
        for path in (REPO / directory).rglob("*"):
            relative = path.relative_to(REPO)
            if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(relative.parts):
                candidates.add(relative.as_posix())
    for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                 "global.json", "NuGet.Config", "scripts/writer-handoff.py",
                 "scripts/windows-writer-handoff.ps1", "scripts/windows-native-evidence.ps1",
                 "scripts/windows-native-process.ps1", "fixtures/golden/manifest.json",
                 "fixtures/golden/fat12.img.gz", "fixtures/golden/fat16.img.gz",
                 "fixtures/golden/fat32.img.gz", "fixtures/golden/ntfs.img.gz"):
        if (REPO / name).is_file():
            candidates.add(name)
    return [{"file": name, "sha256": digest(REPO / name)} for name in sorted(candidates)]


def prepare(root, no_build=False, configuration="Debug"):
    require(sys.platform.startswith("linux"), "Independent input preparation requires Linux")
    require(not any(key.startswith("SECTORWORKS_WRITER_") for key in os.environ),
            "Clear existing handoff environment variables before orchestration")
    require(not root.exists() and not root.is_symlink(), "Preparation root must be new")
    native_tools = tools()
    snapshot = source_files()
    root.mkdir(parents=True)
    inputs = root / "inputs"
    inputs.mkdir()
    results = root / "linux-preparation"
    results.mkdir()
    report = {"schema": 1, "status": "pending", "nativeTools": native_tools, "sourceFiles": snapshot}
    try:
        environment = dict(os.environ, SECTORWORKS_WRITER_EXPORT=str(inputs.absolute()))
        command = [dotnet(), "test", PROJECT, "--no-restore", "--configuration", configuration, "--filter", SAVE_FILTER,
                   "--logger", "trx;LogFileName=linux-inputs.trx",
                   "--results-directory", str(results.absolute())]
        if no_build:
            command.insert(3, "--no-build")
            assembly = REPO / f"tests/SectorWorks.StorageTests/bin/{configuration}/net10.0/SectorWorks.StorageTests.dll"
            report["prebuiltStorageTestAssemblySha256"] = digest(assembly)
        report["command"] = command
        with open(results / "dotnet.log", "xb") as log:
            completed = subprocess.run(command, cwd=REPO, env=environment, stdout=log,
                                       stderr=subprocess.STDOUT, timeout=1200, check=False)
        report["exitCode"] = completed.returncode
        require(completed.returncode == 0, "Linux input authoring/managed/native tests failed; retain evidence")
        trx(results / "linux-inputs.trx", 84)
        require(source_files() == snapshot, "Source changed during Linux preparation; use a fresh stable run")
        entries = []
        for row in rows():
            entry = load(child(inputs, row + ".json"))
            require(entry["row"] == row and entry["file"] == row + ".disk", "Exported row identity mismatch")
            verify_file(child(inputs, entry["file"]), entry["length"], entry["sha256"])
            entries.append(entry)
        require(len(list(inputs.glob("*.json"))) == 84, "Unexpected exported rows")
        catalog = {"schema": 1, "status": "linux-inputs-verified", "inputs": entries,
                   "sourceFiles": snapshot, "nativeTools": native_tools,
                   "linuxTrxSha256": digest(results / "linux-inputs.trx")}
        write_new(inputs / "catalog.json", catalog)
        catalog_sha = digest(inputs / "catalog.json")
        (root / "catalog.sha256").write_text(catalog_sha + "\n", encoding="ascii")
        report.update(status="passed", catalogSha256=catalog_sha, rowCount=84)
        print("84 Linux-authored/checked inputs ready; catalog SHA256 " + catalog_sha)
    except Exception as error:
        report.update(status="failed", error=str(error))
        raise
    finally:
        write_new(root / "preparation.json", report)


def volume_bounds(raw, layout):
    length = raw.stat().st_size
    with open(raw, "rb") as stream:
        header = stream.read(512)
        require(len(header) == 512 and header[510:] == b"\x55\xaa", "Missing disk/volume boot signature")
        if layout == "filesystem":
            return 0, length
        if layout == "mbr":
            require(header[450] != 0 and header[450] != 0xee, "Unexpected first MBR partition")
            start, sectors = struct.unpack_from("<II", header, 454)
        elif layout == "gpt":
            require(header[450] == 0xee, "Missing protective MBR")
            gpt = bytearray(stream.read(512))
            require(gpt[:8] == b"EFI PART", "Missing GPT")
            size, checksum = struct.unpack_from("<II", gpt, 12)
            require(92 <= size <= 512, "Invalid GPT header length")
            struct.pack_into("<I", gpt, 16, 0)
            require(zlib.crc32(gpt[:size]) == checksum, "GPT header CRC mismatch")
            entries, count, stride, crc = struct.unpack_from("<QIII", gpt, 72)
            require(0 < count <= 1024 and stride == 128 and entries * 512 + count * stride <= length,
                    "Invalid GPT table bounds")
            stream.seek(entries * 512)
            table = stream.read(count * stride)
            require(zlib.crc32(table) == crc and table[:16] != bytes(16), "GPT table CRC/first entry mismatch")
            start, last = struct.unpack_from("<QQ", table, 32)
            sectors = last - start + 1
        else:
            raise ValueError("Unknown layout")
    require(start > 0 and sectors > 0 and (start + sectors) * 512 <= length, "Invalid partition bounds")
    return start * 512, sectors * 512


def native_check(entry, output, scratch, native_tools, log):
    container, layout, filesystem, _, _ = entry["row"].split(".")
    raw = scratch / "converted.raw"
    if container == "raw":
        raw = output
    else:
        run([native_tools["qemu-img"]["path"], "convert", "-q", "-f",
             "vhdx" if container.startswith("vhdx") else "vpc", "-O", "raw", str(output), str(raw)], log)
    offset, length = volume_bounds(raw, layout)
    require((offset, length) == (entry["volumeOffset"], entry["volumeLength"]),
            "Independent partition bounds disagree with managed output")
    volume = scratch / "volume.img"
    with open(raw, "rb") as source, open(volume, "xb") as target:
        source.seek(offset)
        remaining = length
        while remaining:
            data = source.read(min(65536, remaining))
            require(bool(data), "Truncated native-check input")
            target.write(data)
            remaining -= len(data)
    tool = lambda name, *args: run([native_tools[name]["path"], *map(str, args)], log)
    if filesystem.startswith("fat"):
        tool("fsck.fat", "-n", volume)
        tool("mdir", "-i", volume, "::/")
        if entry["workflow"] in ("growth-first", "growth-second"):
            length = 20 * 1024 * 1024 + 17
            full, tail = divmod(length, 251)
            expected = hashlib.sha256(bytes(range(251)) * full + bytes(range(tail))).digest()
            count = 1 if entry["workflow"] == "growth-first" else 2
            for index in range(1, count + 1):
                actual = tool("mtype", "-i", volume, f"::/GROWTH{index}.BIN")
                require(len(actual) == length and hashlib.sha256(actual).digest() == expected,
                        "Independently extracted growth payload differs")
            return
        expected = b"overlay bytes\n" if entry["workflow"] == "saveas" else b"committed bytes\n"
        require(tool("mtype", "-i", volume, "::/Existing long \u03bb.txt") == expected, "FAT replacement differs")
        if entry["workflow"] == "saveas":
            require(tool("mtype", "-i", volume, "::/Renamed directory/Added-\u03bb.txt") == expected,
                    "FAT nested addition differs")
    else:
        tool("ntfsfix", "-n", volume)
        if entry["workflow"] == "commit":
            require(tool("ntfscat", volume, "/SENTINEL.TXT") == b"committed NTFS overlay\n",
                    "NTFS committed data differs")
        else:
            tool("ntfsinfo", "-m", volume)
            tool("ntfsinfo", "-F", "/Renamed \u03bb directory/ADDED \u03bb.txt", volume)
            tool("ntfscluster", "-F", "/SENTINEL.TXT", volume)
            payload = bytes(index % 251 for index in range(20000))
            for path in ("/SENTINEL.TXT", "/Renamed \u03bb directory/Large.bin"):
                require(tool("ntfscat", volume, path) == payload, "NTFS nonresident data differs")
            for path in ("/LARGE.BIN", "/Renamed \u03bb directory/ADDED \u03bb.txt"):
                require(tool("ntfscat", volume, path) == b"managed NTFS overlay\n", "NTFS resident data differs")


def verify(inputs, outputs, report_path, growth=False):
    require(sys.platform.startswith("linux"), "Independent output verification requires Linux")
    require(not report_path.exists(), "Do not overwrite previous verification evidence")
    selected_rows = growth_rows() if growth else rows()
    workflows = ("growth-first", "growth-second") if growth else ("saveas", "commit")
    expected_tests = 9 if growth else 168
    report = {"schema": 1, "status": "pending", "mode": "growth" if growth else "matrix", "outputs": [
        {"row": row, "workflow": workflow, "status": "pending", "commands": []}
        for row in selected_rows for workflow in workflows]}
    try:
        native_tools = tools()
        report["nativeTools"] = native_tools
        catalog_path = child(inputs, "catalog.json")
        catalog = load(catalog_path)
        catalog_sha = digest(catalog_path)
        preparation = load(child(inputs.parent, "preparation.json"))
        require(preparation["status"] == "passed" and
                preparation["catalogSha256"].upper() == catalog_sha and
                (inputs.parent / "catalog.sha256").read_text(encoding="ascii").strip().upper() == catalog_sha,
                "Original successful Linux preparation/hash record is required")
        linux_trx = inputs.parent / "linux-preparation" / "linux-inputs.trx"
        require(digest(linux_trx) == catalog["linuxTrxSha256"].upper(), "Linux preparation TRX changed")
        trx(linux_trx, 84)
        require(catalog["schema"] == 1 and catalog["status"] == "linux-inputs-verified",
                "Unqualified input catalog")
        entries = {item["row"]: item for item in catalog["inputs"]}
        require(len(catalog["inputs"]) == len(entries) == 84 and sorted(entries) == rows(),
                "Incomplete/duplicate input rows")
        for row, entry in entries.items():
            require(entry["file"] == row + ".disk", "Input filename mismatch")
            verify_file(child(inputs, entry["file"]), entry["length"], entry["sha256"])
        windows = load(child(outputs, "windows-run.json"))
        require(windows["status"] == "pending-independent-linux-validation" and
                windows["exitCode"] == 0 and windows["catalogSha256"].upper() == catalog_sha and
                windows["platform"] == "Win32NT" and windows.get("mode", "matrix") == report["mode"],
                "Windows workflow run missing/failed or catalog changed")
        result_path = child(outputs, "windows.trx")
        require(digest(result_path) == windows["trxSha256"].upper(), "Returned TRX hash mismatch")
        trx(result_path, expected_tests)
        expected_names = {f"{row}.{workflow}.json" for row in selected_rows for workflow in workflows}
        actual_names = {path.name for path in outputs.glob("*.json")} - {"windows-run.json"}
        require(expected_names == actual_names, "Missing/duplicate/unexpected queued output rows")
        require({path.name for path in outputs.glob("*.disk")} ==
                {name[:-5] + ".disk" for name in expected_names}, "Missing/unexpected output images")
        report.update(catalogSha256=catalog_sha, windowsTrxSha256=digest(result_path))
        scratch_root = report_path.parent / (report_path.stem + "-work")
        require(not scratch_root.exists(), "Native verification scratch root must be new")
        scratch_root.mkdir()
        for name in sorted(expected_names):
            entry = load(child(outputs, name))
            row = name.rsplit(".", 2)[0]
            workflow = name.rsplit(".", 2)[1]
            result = next(item for item in report["outputs"] if item["row"] == row and item["workflow"] == workflow)
            try:
                require(entry["schema"] == 1 and entry["status"] == "pending-independent-linux-validation" and
                        entry["row"] == row and entry["workflow"] == workflow and
                        entry["file"] == name[:-5] + ".disk" and
                        entry["inputSha256"].upper() == entries[row]["sha256"].upper() and
                        entry["catalogSha256"].upper() == catalog_sha and
                        "Windows" in entry["platform"], "Queue row/provenance mismatch")
                output = child(outputs, entry["file"])
                verify_file(output, entry["length"], entry["sha256"])
                if growth:
                    previous = entries[row]["length"]
                    if workflow == "growth-second":
                        previous = load(child(outputs, row + ".growth-first.json"))["length"]
                    require(entry["length"] > previous, "Retained output did not physically grow")
                result.update(sha256=entry["sha256"], length=entry["length"],
                              inputSha256=entry["inputSha256"])
                scratch = scratch_root / name[:-5]
                scratch.mkdir()
                native_check(entry, output, scratch, native_tools, result["commands"])
                verify_file(output, entry["length"], entry["sha256"])
                result["status"] = "passed"
                shutil.rmtree(scratch)
            except Exception as error:
                result.update(status="failed", error=str(error))
                raise
        scratch_root.rmdir()
        report["status"] = "passed"
        print(f"{len(expected_names)}/{len(expected_names)} exact Windows outputs independently passed Linux checks; no skips.")
    except Exception as error:
        report.update(status="failed", error=str(error))
        raise
    finally:
        write_new(report_path, report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    prepare_parser.add_argument("--root", type=Path, required=True)
    prepare_parser.add_argument("--no-build", action="store_true",
                                help="Reuse the already verified test build to release shared build slots")
    prepare_parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    verify_parser = commands.add_parser("verify")
    verify_parser.add_argument("--inputs", type=Path, required=True)
    verify_parser.add_argument("--outputs", type=Path, required=True)
    verify_parser.add_argument("--report", type=Path, required=True)
    verify_parser.add_argument("--growth", action="store_true", help="Verify the separate 9-case/18-output growth regression")
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.root.absolute(), args.no_build, args.configuration)
    else:
        verify(args.inputs.absolute(), args.outputs.absolute(), args.report.absolute(), args.growth)


if __name__ == "__main__":
    main()
