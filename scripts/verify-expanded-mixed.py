#!/usr/bin/env python3
"""Independent checks of the exact Windows mixed-volume supplemental outputs."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import sys

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('expanded', Path(__file__).with_name('expanded-writer-handoff.py'))
expanded = importlib.util.module_from_spec(spec)
spec.loader.exec_module(expanded)
base = expanded.base
observer_spec = importlib.util.spec_from_file_location('ntfs_observer', Path(__file__).with_name('inspect-windows-ntfs.py'))
observer = importlib.util.module_from_spec(observer_spec)
observer_spec.loader.exec_module(observer)


def expected_rows():
    return sorted({row.split('.')[2] for row in expanded.rows()})


def outside(path, first, length):
    h = hashlib.sha256()
    with path.open('rb') as file:
        for start, count in ((0, first), (first + length, path.stat().st_size - first - length)):
            base.require(count >= 0, 'Invalid preserved range')
            file.seek(start)
            while count:
                chunk = file.read(min(65536, count))
                base.require(bool(chunk), 'Truncated preserved range')
                h.update(chunk)
                count -= len(chunk)
    return h.hexdigest().upper()


def validate_run(root, source_seal, prerequisite):
    run = base.load(root / 'windows-run.json')
    base.require(run['schema'] == 1 and run['status'] == 'pending-independent-linux-validation'
                 and run['platform'] == 'Win32NT' and run['exitCode'] == 0
                 and run['sourceArchiveSha256'] == source_seal.upper(), 'Windows mixed source/run identity mismatch')
    # The larger independently passed matrix is a prerequisite, not a substitute
    # for checking these exact composed outputs and their untouched sibling.
    base.require(prerequisite.stat().st_size <= 32 * 1024 * 1024, 'Oversized prerequisite')
    proof = json.loads(prerequisite.read_text())
    base.require(base.digest(prerequisite) == run['saveAsPrerequisiteSha256']
                 and proof['status'] == 'passed' and proof['workflow'] == 'saveas'
                 and len(proof['outputs']) == 441
                 and all(item['status'] == 'passed' for item in proof['outputs']), 'Missing mixed writer prerequisite')
    trx = root / 'windows.trx'
    base.require(base.digest(trx) == run['trxSha256'], 'Mixed TRX changed')
    base.trx(trx, 21)
    expected = expected_rows()
    base.require({p.stem for p in root.glob('*.json')} - {'windows-run'} == set(expected), 'Incomplete mixed rows')
    base.require({p.name for p in root.glob('*.disk')} == {row + '.' + phase + '.disk'
                 for row in expected for phase in ('input', 'saveas', 'commit')}, 'Incomplete mixed exact outputs')
    return run


def volume_state(path, start):
    with path.open('rb') as file:
        file.seek(start)
        boot = file.read(512)
        base.require(boot[3:11] == b'NTFS    ' and boot[11:14] == b'\x00\x02\x08'
                     and boot[64] == 246, 'Unexpected supplemental NTFS geometry')
        mft = struct.unpack_from('<Q', boot, 48)[0] * 4096
        base.require(0 <= mft <= 33554432 - 4096, 'Supplemental MFT prefix outside volume')
        file.seek(start + mft + 3072)
        record = observer.fixed_record(file.read(1024))
    position, used = struct.unpack_from('<H', record, 20)[0], struct.unpack_from('<I', record, 24)[0]
    for _ in range(128):
        kind, length = struct.unpack_from('<II', record, position)
        if kind == 0xffffffff:
            break
        base.require(24 <= length <= used - position and position + length <= len(record), 'Volume attribute outside record')
        if kind == 0x70:
            value_length = struct.unpack_from('<I', record, position + 16)[0]
            value_start = struct.unpack_from('<H', record, position + 20)[0]
            base.require(record[position + 8] == 0 and value_length == 12
                         and value_start + value_length <= length, 'Unexpected volume information')
            value = record[position + value_start:position + value_start + 12]
            return value[8], value[9], struct.unpack_from('<H', value, 10)[0]
        position += length
    raise ValueError('Missing volume information')


def copy_volume(disk, destination, offset, length):
    with disk.open('rb') as source, destination.open('xb') as target:
        source.seek(offset)
        while length:
            chunk = source.read(min(65536, length))
            base.require(bool(chunk), 'Truncated NTFS partition')
            target.write(chunk)
            length -= len(chunk)


def inspect(volume, log):
    data = base.run([sys.executable, str(Path(__file__).with_name('inspect-windows-ntfs.py')), str(volume)], log)
    return json.loads(data)


def user_metadata(inventory):
    # These controls add only a root directory. Existing ordinary records,
    # including their SI, per-file security and all filename/alias values, must
    # retain exact bytes. Physical extension ownership is checked by the product
    # resolver and the separate malformed/ownership tests, not inferred here.
    result = {}
    for record in inventory['records']:
        if record['id'] >= 16 and not record['base_reference'] and record['names']:
            result[record['id']] = dict(sequence=record['sequence'], flags=record['record_flags'], names=record['names'],
                attributes=sorted((a['type'], a['name'], a['data_sha256']) for a in record['attributes']
                                  if a['type'] in ('0x10', '0x30', '0x50')))
    return result


def verify(root, report_path, source_seal, prerequisite):
    base.require(sys.platform.startswith('linux') and not report_path.exists(), 'Fresh Linux verification report required')
    report = dict(schema=1, status='pending', sourceArchiveSha256=source_seal.upper(), outputs=[])
    try:
        report['windowsRun'] = validate_run(root, source_seal, prerequisite)
        tools = base.tools()
        report['nativeTools'] = tools
        scratch_root = report_path.parent / (report_path.stem + '-work')
        scratch_root.mkdir()
        for row in expected_rows():
            entry = base.load(root / (row + '.json'))
            base.require(entry['schema'] == 1 and entry['status'] == 'pending-independent-linux-validation'
                         and entry['row'] == row and len(entry['files']) == 3, 'Mixed queue identity mismatch')
            files = {item['workflow']: item for item in entry['files']}
            base.require(set(files) == {'input', 'saveas', 'commit'}, 'Duplicate mixed workflow')
            result = dict(row=row, status='pending', commands=[])
            report['outputs'].append(result)
            scratch = scratch_root / row
            scratch.mkdir()
            input_metadata = None
            for phase in ('input', 'saveas', 'commit'):
                item = files[phase]
                base.require(item['file'] == row + '.' + phase + '.disk', 'Mixed file identity mismatch')
                path = base.child(root, item['file'])
                base.verify_file(path, item['length'], item['sha256'])
                with path.open('rb') as file:
                    mbr = file.read(512)
                base.require(mbr[510:512] == b'\x55\xaa' and mbr[450] == mbr[466] == 7, 'Mixed MBR differs')
                first, sectors = struct.unpack_from('<II', mbr, 454)
                second, second_sectors = struct.unpack_from('<II', mbr, 470)
                first *= 512
                second *= 512
                length = sectors * 512
                base.require((first, second, length, second_sectors * 512, item['length'])
                             == (1048576, 36700160, 33554432, 33554432, 71303168), 'Independent mixed bounds differ')
                base.require((entry['first'], entry['second'], entry['volumeLength']) == (first, second, length), 'Managed mixed bounds disagree')
                expected_version = (int(row[4]), int(row[6]))
                base.require(volume_state(path, first) == (*expected_version, 0), 'Primary version or clean state differs')
                base.require(volume_state(path, second) == (3, 1, 1), 'Sibling is not the explicitly dirty ordinary NTFS control')
                base.require(outside(path, first, length) == entry['untouched'], 'Dirty sibling or opaque regions changed')
                if phase == 'input':
                    base.require(item['sha256'] == entry['original'], 'Original mixed baseline differs')
                else:
                    base.require(item['sha256'] != entry['original'], 'Expected mixed edit is absent')
                volume = scratch / (phase + '.volume')
                copy_volume(path, volume, first, length)
                base.run([tools['ntfsfix']['path'], '-n', str(volume)], result['commands'])
                base.run([tools['ntfsinfo']['path'], '-m', str(volume)], result['commands'])
                inventory = inspect(volume, result['commands'])
                metadata = user_metadata(inventory)
                if phase == 'input':
                    input_metadata = metadata
                else:
                    base.require(all(metadata.get(k) == v for k, v in input_metadata.items()), 'Retained raw SI/security/filename metadata changed')
                    base.run([tools['ntfsinfo']['path'], '-F', '/MIXED SAFE', str(volume)], result['commands'])
                base.require(base.run([tools['ntfscat']['path'], str(volume), '/Legacy/NOTE.TXT'], result['commands'])
                             == b'Synthetic legacy NTFS layout probe.\n', 'Mixed retained file content differs')
                base.verify_file(path, item['length'], item['sha256'])
            result.update(status='passed', files=list(files.values()), untouchedSha256=entry['untouched'])
            shutil.rmtree(scratch)
        scratch_root.rmdir()
        report['status'] = 'passed'
        print('21 exact Windows mixed-volume input/Save As/commit sets independently verified; dirty siblings and opaque bytes unchanged.')
    except Exception as error:
        report.update(status='failed', error=str(error))
        raise
    finally:
        base.write_new(report_path, report)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--outputs', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--source-seal', required=True)
    parser.add_argument('--prerequisite', type=Path, required=True)
    args = parser.parse_args()
    verify(args.outputs.absolute(), args.report.absolute(), args.source_seal, args.prerequisite.absolute())
