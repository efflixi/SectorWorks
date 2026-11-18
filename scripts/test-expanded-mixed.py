#!/usr/bin/env python3
"""Supplemental mixed-output source/TRX/prerequisite guards; no native tools."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('mixed', Path(__file__).with_name('verify-expanded-mixed.py'))
mixed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mixed)
base = mixed.base


class MixedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='sectorworks-mixed-guards-')
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def fixture(self):
        proof = self.root / 'proof.json'
        proof.write_text(json.dumps(dict(status='passed', workflow='saveas', outputs=[dict(status='passed')] * 441)))
        trx = self.root / 'windows.trx'
        trx.write_text('<TestRun><ResultSummary><Counters total="21" passed="21"/></ResultSummary><Results>'
                       + '<UnitTestResult outcome="Passed"/>' * 21 + '</Results></TestRun>')
        run = dict(schema=1, status='pending-independent-linux-validation', platform='Win32NT', exitCode=0,
                   sourceArchiveSha256='A' * 64, saveAsPrerequisiteSha256=base.digest(proof), trxSha256=base.digest(trx))
        for row in mixed.expected_rows():
            (self.root / (row + '.json')).write_text('{}')
            for phase in ('input', 'saveas', 'commit'):
                (self.root / (row + '.' + phase + '.disk')).write_bytes(b'x')
        (self.root / 'windows-run.json').write_text(json.dumps(run))
        # The proof is outside the returned queue root in a real run.
        external = self.root.parent / (self.root.name + '-proof.json')
        proof.rename(external)
        self.addCleanup(external.unlink)
        return run, external

    def test_all_21_feature_sets_require_each_exact_input_and_both_outputs(self):
        run, proof = self.fixture()
        self.assertEqual(21, len(mixed.expected_rows()))
        self.assertEqual(run, mixed.validate_run(self.root, 'A' * 64, proof))
        next(self.root.glob('*.commit.disk')).unlink()
        with self.assertRaisesRegex(ValueError, 'exact outputs'):
            mixed.validate_run(self.root, 'A' * 64, proof)

    def test_wrong_source_failed_run_and_unverified_prerequisite_are_refused(self):
        run, proof = self.fixture()
        with self.assertRaisesRegex(ValueError, 'identity mismatch'):
            mixed.validate_run(self.root, 'B' * 64, proof)
        run['exitCode'] = 1
        (self.root / 'windows-run.json').write_text(json.dumps(run))
        with self.assertRaisesRegex(ValueError, 'identity mismatch'):
            mixed.validate_run(self.root, 'A' * 64, proof)
        run['exitCode'] = 0
        proof.write_text(json.dumps(dict(status='pending', workflow='saveas', outputs=[dict(status='passed')] * 441)))
        run['saveAsPrerequisiteSha256'] = base.digest(proof)
        (self.root / 'windows-run.json').write_text(json.dumps(run))
        with self.assertRaisesRegex(ValueError, 'prerequisite'):
            mixed.validate_run(self.root, 'A' * 64, proof)

    def test_preservation_hash_covers_headers_gaps_sibling_and_tail(self):
        path = self.root / 'disk'
        path.write_bytes(b'header-primary-sibling-tail')
        before = mixed.outside(path, 7, 7)
        data = bytearray(path.read_bytes())
        data[9] ^= 1
        path.write_bytes(data)
        self.assertEqual(before, mixed.outside(path, 7, 7))
        for position in (0, 14, len(data) - 1):
            changed = bytearray(data)
            changed[position] ^= 1
            path.write_bytes(changed)
            self.assertNotEqual(before, mixed.outside(path, 7, 7))


if __name__ == '__main__':
    unittest.main()
