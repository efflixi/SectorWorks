#!/usr/bin/env python3
"""No external tools needed for handoff verifier guard regressions."""
import importlib.util
from pathlib import Path
import shutil
import struct
import unittest
from unittest.mock import patch
import uuid

spec = importlib.util.spec_from_file_location("handoff", Path(__file__).with_name("writer-handoff.py"))
handoff = importlib.util.module_from_spec(spec)
spec.loader.exec_module(handoff)


class HandoffTests(unittest.TestCase):
    def setUp(self):
        self.root = Path("artifacts") / ("handoff-regression-" + uuid.uuid4().hex)
        self.root.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.root)

    def test_matrix_has_exactly_84_distinct_inputs_and_168_workflows(self):
        self.assertEqual(84, len(set(handoff.rows())))
        self.assertEqual(63, sum(".fat" in row for row in handoff.rows()))
        self.assertEqual(21, sum(".ntfs." in row for row in handoff.rows()))
        self.assertEqual(9, len(handoff.growth_rows()))

    def test_unsafe_paths_fail(self):
        for name in ("", ".", "..", "../input", "sub\\input", "C:input", "/input"):
            with self.assertRaises(ValueError):
                handoff.child(self.root, name)

    def test_changed_image_fails_hash_or_length(self):
        image = self.root / "input.disk"
        image.write_bytes(b"expected")
        sha = handoff.digest(image)
        handoff.verify_file(image, 8, sha)
        image.write_bytes(b"tampered")
        with self.assertRaises(ValueError):
            handoff.verify_file(image, 8, sha)
        with self.assertRaises(ValueError):
            handoff.verify_file(image, handoff.MAX_IMAGE + 1, sha)

    def test_links_fail(self):
        (self.root / "ordinary").write_bytes(b"data")
        (self.root / "link").symlink_to("ordinary")
        with self.assertRaises(ValueError):
            handoff.child(self.root, "link")

    def test_failed_or_incomplete_trx_cannot_qualify(self):
        path = self.root / "result.trx"
        path.write_text('<TestRun><ResultSummary><Counters total="168" passed="168"/></ResultSummary>'
                        '<Results><UnitTestResult outcome="Passed"/></Results></TestRun>')
        with self.assertRaises(ValueError):
            handoff.trx(path, 168)

    def test_new_evidence_never_overwrites_failed_run(self):
        path = self.root / "verification.json"
        handoff.write_new(path, {"status": "failed"})
        with self.assertRaises(FileExistsError):
            handoff.write_new(path, {"status": "passed"})
        self.assertEqual("failed", handoff.load(path)["status"])

    def test_independent_mbr_bounds_not_managed_claim(self):
        raw = self.root / "disk.raw"
        data = bytearray(4096)
        data[510:512] = b"\x55\xaa"
        data[450] = 0x07
        struct.pack_into("<II", data, 454, 1, 7)
        raw.write_bytes(data)
        self.assertEqual((512, 3584), handoff.volume_bounds(raw, "mbr"))
        struct.pack_into("<II", data, 454, 1, 8)
        raw.write_bytes(data)
        with self.assertRaises(ValueError):
            handoff.volume_bounds(raw, "mbr")

    def fixture(self):
        inputs = self.root / "inputs"
        outputs = self.root / "returned"
        inputs.mkdir()
        outputs.mkdir()
        linux_results = self.root / "linux-preparation"
        linux_results.mkdir()
        linux_trx = linux_results / "linux-inputs.trx"
        windows_trx = outputs / "windows.trx"
        for path, count in ((linux_trx, 84), (windows_trx, 168)):
            path.write_text(f'<TestRun><ResultSummary><Counters total="{count}" passed="{count}"/>'
                            '</ResultSummary><Results>' +
                            '<UnitTestResult outcome="Passed"/>' * count + '</Results></TestRun>')
        entries = []
        for row in handoff.rows():
            image = inputs / (row + ".disk")
            image.write_bytes(b"x")
            entries.append({"row": row, "file": image.name, "length": 1, "sha256": handoff.digest(image)})
        handoff.write_new(inputs / "catalog.json",
                          {"schema": 1, "status": "linux-inputs-verified", "inputs": entries,
                           "linuxTrxSha256": handoff.digest(linux_trx)})
        sha = handoff.digest(inputs / "catalog.json")
        (self.root / "catalog.sha256").write_text(sha)
        handoff.write_new(self.root / "preparation.json", {"status": "passed", "catalogSha256": sha})
        handoff.write_new(outputs / "windows-run.json",
                          {"status": "pending-independent-linux-validation", "exitCode": 0,
                           "catalogSha256": sha, "platform": "Win32NT", "trxSha256": handoff.digest(windows_trx)})
        return inputs, outputs, sha, entries

    def test_missing_queue_cannot_turn_managed_success_into_a_pass(self):
        inputs, outputs, _, _ = self.fixture()
        report = self.root / "missing-queue.json"
        with patch.object(handoff, "tools", return_value={}):
            with self.assertRaisesRegex(ValueError, "queued output rows"):
                handoff.verify(inputs, outputs, report)
        result = handoff.load(report)
        self.assertEqual("failed", result["status"])
        self.assertEqual(168, sum(item["status"] == "pending" for item in result["outputs"]))

    def test_tampered_return_fails_before_native_commands(self):
        inputs, outputs, sha, entries = self.fixture()
        for entry in entries:
            for workflow in ("commit", "saveas"):
                name = entry["row"] + "." + workflow
                (outputs / (name + ".disk")).write_bytes(b"x")
                handoff.write_new(outputs / (name + ".json"),
                                  {"schema": 1, "status": "pending-independent-linux-validation",
                                   "row": entry["row"], "workflow": workflow,
                                   "catalogSha256": sha, "inputSha256": entry["sha256"],
                                   "file": name + ".disk", "length": 1, "sha256": entry["sha256"],
                                   "platform": "Microsoft Windows", "volumeOffset": 0, "volumeLength": 1})
        first = sorted(outputs.glob("*.disk"))[0]
        first.write_bytes(b"y")
        report = self.root / "tampered-output.json"
        with patch.object(handoff, "tools", return_value={}), patch.object(handoff, "native_check") as native:
            with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
                handoff.verify(inputs, outputs, report)
            native.assert_not_called()
        result = handoff.load(report)
        self.assertEqual(1, sum(item["status"] == "failed" for item in result["outputs"]))
        self.assertEqual(167, sum(item["status"] == "pending" for item in result["outputs"]))

    def test_missing_native_tool_is_an_explicit_failed_report(self):
        report = self.root / "missing-tool.json"
        with patch.object(handoff, "tools", side_effect=ValueError("Missing mandatory independent tool")):
            with self.assertRaisesRegex(ValueError, "Missing mandatory"):
                handoff.verify(self.root, self.root, report)
        self.assertEqual("failed", handoff.load(report)["status"])


if __name__ == "__main__":
    unittest.main()
