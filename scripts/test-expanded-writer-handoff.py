#!/usr/bin/env python3
"""Guard regressions for the distinct expanded-NTFS handoff; no native tools."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('expanded', Path(__file__).with_name('expanded-writer-handoff.py'))
handoff = importlib.util.module_from_spec(spec)
spec.loader.exec_module(handoff)
base = handoff.base


class ExpandedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='sectorworks-expanded-guards-')
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def fixture(self):
        inputs = self.root / 'inputs'
        inputs.mkdir()
        results = self.root / 'linux-preparation'
        results.mkdir()
        trx = results / 'linux-inputs.trx'
        trx.write_text('<TestRun><ResultSummary><Counters total="441" passed="441"/></ResultSummary><Results>'
                       + '<UnitTestResult outcome="Passed"/>' * 441 + '</Results></TestRun>')
        entries = []
        for row in handoff.rows():
            path = inputs / (row + '.disk')
            path.write_bytes(b'x')
            entries.append(dict(row=row, file=path.name, length=1, sha256=base.digest(path)))
        catalog = dict(schema=2, status='linux-expanded-inputs-verified', inputs=entries, linuxTrxSha256=base.digest(trx))
        (inputs / 'catalog.json').write_text(json.dumps(catalog))
        self.seal(inputs)
        return inputs, entries

    def seal(self, inputs):
        sha = base.digest(inputs / 'catalog.json')
        (self.root / 'preparation.json').write_text(json.dumps(dict(status='passed', catalogSha256=sha)))
        (self.root / 'catalog.sha256').write_text(sha)
        return sha

    def test_exact_matrix_includes_420_new_and_21_existing_rows(self):
        rows = handoff.rows()
        self.assertEqual(441, len(rows))
        self.assertEqual(441, len(set(rows)))
        self.assertEqual(21, sum('.ntfs3-1-ordinary.' in row for row in rows))
        self.assertEqual(126, sum('.p4096.' in row for row in rows))

    def test_independently_sealed_catalog_accepts_all_441_exact_inputs(self):
        inputs, entries = self.fixture()
        sha, actual = handoff.validate_catalog(inputs)
        self.assertEqual(441, len(actual))
        self.assertEqual(base.digest(inputs / 'catalog.json'), sha)
        self.assertEqual(entries[0], actual[entries[0]['row']])

    def test_duplicate_wrong_or_old_schema_cannot_become_qualified(self):
        inputs, _ = self.fixture()
        path = inputs / 'catalog.json'
        original = json.loads(path.read_text())
        for change in ('duplicate', 'wrong', 'schema', 'pending'):
            value = json.loads(json.dumps(original))
            if change == 'duplicate':
                value['inputs'][1] = value['inputs'][0]
            elif change == 'wrong':
                value['inputs'][1]['row'] = 'unqualified-row'
            elif change == 'schema':
                value['schema'] = 1
            else:
                value['status'] = 'pending'
            path.write_text(json.dumps(value))
            self.seal(inputs)
            with self.assertRaisesRegex(ValueError, 'catalog'):
                handoff.validate_catalog(inputs)

    def test_changed_preparation_trx_or_input_fails(self):
        inputs, entries = self.fixture()
        first = inputs / entries[0]['file']
        first.write_bytes(b'y')
        with self.assertRaisesRegex(ValueError, 'SHA256'):
            handoff.validate_catalog(inputs)
        first.write_bytes(b'x')
        (self.root / 'linux-preparation/linux-inputs.trx').write_text('<failed/>')
        with self.assertRaisesRegex(ValueError, 'TRX changed'):
            handoff.validate_catalog(inputs)

    def test_missing_queues_keep_managed_success_unqualified(self):
        inputs, _ = self.fixture()
        outputs = self.root / 'outputs'
        outputs.mkdir()
        trx = outputs / 'windows.trx'
        trx.write_bytes((self.root / 'linux-preparation/linux-inputs.trx').read_bytes())
        (outputs / 'windows-run.json').write_text(json.dumps(dict(
            schema=2, workflow='saveas', status='pending-independent-linux-validation', exitCode=0,
            platform='Win32NT', catalogSha256=base.digest(inputs / 'catalog.json'), trxSha256=base.digest(trx))))
        report = self.root / 'verification.json'
        with patch.object(base, 'tools', return_value={}), patch.object(handoff, 'native_check') as native:
            with self.assertRaisesRegex(ValueError, 'output queues'):
                handoff.verify(inputs, outputs, report, 'saveas')
            native.assert_not_called()
        self.assertEqual('failed', json.loads(report.read_text())['status'])


if __name__ == '__main__':
    unittest.main()
