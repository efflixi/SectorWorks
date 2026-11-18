#!/usr/bin/env python3
"""Synthetic CLFS envelope failures; no native/owner media or recovery engine."""
import hashlib
import importlib.util
import pathlib
import struct
import sys
import tempfile
import unittest
from unittest import mock
import zlib


sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location("inspect_clfs", pathlib.Path(__file__).with_name("inspect-clfs.py"))
CLFS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CLFS)


def put(data, offset, value, width=4):
    data[offset:offset + width] = value.to_bytes(width, "little")


def checksum(block):
    put(block, 12, 0)
    put(block, 12, zlib.crc32(block))
    return block


def protect(data):
    fixups = int.from_bytes(data[104:108], "little")
    for index in range(len(data) // 512):
        tail = (index + 1) * 512 - 2
        data[fixups + index * 2:fixups + index * 2 + 2] = data[tail:tail + 2]
        data[tail] = 0x10 | (0x40 if index == 0 else 0) | (0x20 if index == len(data) // 512 - 1 else 0)
        data[tail + 1] = 7
    return checksum(data)


def block(size):
    # Independently laid out from the referenced header table: version 1,
    # 112-byte envelope, 512-byte sectors, trailing aligned sector signatures.
    result = bytearray(size)
    struct.pack_into("<HBBHH", result, 0, 0x15, 7, 0, size // 512, size // 512)
    put(result, 16, 1)
    put(result, 40, 112)
    fixups = size - ((size // 512 * 2 + 7) // 8 + 1) * 8
    put(result, 104, fixups)
    return result


def fixture():
    first = block(1024)
    put(first, 112 + 72, 6)
    for index, (offset, size) in enumerate(((0, 1024), (1024, 1024), (2048, 31232),
                                         (33280, 31232), (64512, 512), (65024, 512))):
        struct.pack_into("<III", first, 112 + 76 + 24 * index + 12, size, offset, index)
    copies = []
    for counter in (19, 20):
        data = block(31232)
        put(data, 112, counter)
        put(data, 112 + 4904, 136 + 16)
        put(data, 112 + 292, 1)
        put(data, 112 + 312, 4920)
        info = 112 + 4920
        struct.pack_into("<IIH", data, info, 0xc1fdf007, 136, 1)
        data[info + 136:info + 152] = "TxfLog\0\0".encode("utf-16-le")
        for offset, value in ((48, 0xffffffff00000000), (56, 1), (64, 1), (72, 0x600), (80, 0x400), (88, 0x201)):
            put(data, info + 8 + offset, value, 8)
        copies.append(protect(data))
    return bytearray(protect(first) + bytes(1024) + copies[0] + copies[1] + protect(block(512)) + bytes(512))


def container_block(stream, physical, virtual, payload=b"opaque", sectors=1, kind=0x26):
    data = bytearray(sectors * 512)
    struct.pack_into("<HBBHH", data, 0, 0x15, 3, stream, sectors, sectors)
    struct.pack_into("<I", data, 16, 1)
    struct.pack_into("<Q", data, 24, physical)
    struct.pack_into("<Q", data, 32, 0xffffffff00000000)
    struct.pack_into("<I", data, 40, 112)
    fixups = len(data) - ((sectors * 2 + 7) // 8) * 8
    struct.pack_into("<I", data, 104, fixups)
    struct.pack_into("<QQQIIHHI", data, 112, virtual, 0xffffffff00000000,
                     0xffffffff00000000, 40 + len(payload), 0, 0, 40, kind)
    data[152:152 + len(payload)] = payload
    for sector in range(sectors):
        tail = (sector + 1) * 512 - 2
        data[fixups + sector * 2:fixups + sector * 2 + 2] = data[tail:tail + 2]
        data[tail:tail + 2] = bytes((4 | (0x40 if sector == 0 else 0)
                                   | (0x20 if sector == sectors - 1 else 0), 3))
    return data


def container_fixture():
    blocks = (container_block(2, 0, 0), container_block(1, 512, 0, b"x" * 600, 2),
              container_block(2, 1536, 512), container_block(1, 2048, 1536))
    prefix = b"".join(blocks)
    return bytearray(prefix + bytes(CLFS.MAX_CONTAINER_BYTES - len(prefix)))


class ClfsContainerObservationsTests(unittest.TestCase):
    def test_multiplexed_records_restore_sector_tails_without_claiming_recovery(self):
        raw = container_fixture()
        before = bytes(raw)
        report = CLFS.observe_container(raw)
        self.assertEqual(before, raw)
        self.assertEqual(4, report["record_count"])
        self.assertEqual(2560, report["occupied_prefix_bytes"])
        self.assertEqual(hashlib.sha256(b"x" * 600).hexdigest(),
                         report["blocks"][1]["records"][0]["payload_sha256"])
        self.assertEqual("0x0000000000000600", report["last_physical_restart_candidates"]["1"]["lsn_raw"])
        self.assertFalse(report["writer_qualified"])
        self.assertEqual("unproven", report["persistent_quiescence"])
        self.assertTrue(all(not block["checksum_validated"] for block in report["blocks"]))

    def test_observed_zero_checksum_does_not_detect_opaque_payload_changes(self):
        raw = container_fixture()
        before = CLFS.observe_container(raw)
        raw[152] ^= 1
        after = CLFS.observe_container(raw)
        self.assertNotEqual(before["blocks"][0]["records"][0]["payload_sha256"],
                            after["blocks"][0]["records"][0]["payload_sha256"])
        self.assertFalse(after["blocks"][0]["checksum_validated"])
        self.assertFalse(after["writer_qualified"])

    def test_header_record_fixup_and_virtual_lsn_failures(self):
        cases = ((0, 0x16, 2), (3, 0, 1), (6, 2, 2), (12, 1, 4), (16, 2, 4),
                 (24, 512, 8), (40, 120, 4), (44, 1, 4), (48, 1, 4),
                 (104, 112, 4), (104, 512, 4), (104, 501, 4), (510, 0, 2),
                 (112, 1, 8), (136, 39, 4), (136, 512, 4), (140, 1, 4),
                 (144, 1, 2), (146, 32, 2), (148, 0x36, 4), (148, 2, 4),
                 (148, 0x24, 4), (158, 1, 1), (160, 1, 1))
        for offset, value, width in cases:
            with self.subTest(offset=offset, value=value):
                raw = container_fixture()
                put(raw, offset, value, width)
                with self.assertRaises(ValueError):
                    CLFS.observe_container(raw)

    def test_zero_gaps_owner_pages_recycled_and_truncated_containers_are_refused(self):
        for length in (0, 511, 2097151, 2097153):
            with self.subTest(length=length), self.assertRaises(ValueError):
                CLFS.observe_container(bytes(length))
        raw = container_fixture()
        raw[-512] = 1
        with self.assertRaisesRegex(ValueError, "prefix"):
            CLFS.observe_container(raw)
        raw = container_fixture()
        put(raw, 4, 1024, 2)
        with self.assertRaisesRegex(ValueError, "owner-page"):
            CLFS.observe_container(raw)
        raw = container_fixture()
        put(raw, 512 + 112, 0x100000000, 8)
        with self.assertRaisesRegex(ValueError, "LSN"):
            CLFS.observe_container(raw)

    def test_block_and_record_budgets_are_enforced(self):
        raw = container_fixture()
        for name in ("MAX_CONTAINER_BLOCKS", "MAX_CONTAINER_RECORDS"):
            with self.subTest(name=name), mock.patch.object(CLFS, name, 2):
                with self.assertRaisesRegex(ValueError, "budget"):
                    CLFS.observe_container(raw)

    def test_empty_container_is_an_observation_without_a_restart_or_write_claim(self):
        report = CLFS.observe_container(bytes(CLFS.MAX_CONTAINER_BYTES))
        self.assertEqual([], report["blocks"])
        self.assertEqual({}, report["last_physical_restart_candidates"])
        self.assertFalse(report["writer_qualified"])

    def test_new_stream_can_start_at_its_first_nonzero_physical_offset(self):
        raw = container_fixture()
        put(raw, 512 + 112, 512, 8)
        report = CLFS.observe_container(raw)
        self.assertEqual("0x0000000000000200", report["blocks"][1]["records"][0]["lsn_raw"])
        self.assertEqual("0x0000000000000600", report["blocks"][3]["records"][0]["lsn_raw"])
        self.assertFalse(report["writer_qualified"])

    def test_sealed_container_file_is_unchanged_and_hash_mismatch_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "generated-container.bin"
            raw = container_fixture()
            path.write_bytes(raw)
            digest = hashlib.sha256(raw).hexdigest()
            self.assertEqual(digest, CLFS.inspect_file(path, digest, container=True)["unchanged_source_sha256"])
            self.assertEqual(raw, path.read_bytes())
            with self.assertRaisesRegex(ValueError, "SHA256"):
                CLFS.inspect_file(path, "0" * 64, container=True)


class ClfsObservationsTests(unittest.TestCase):
    def test_unknown_base_suffix_is_not_silently_ignored(self):
        for suffix in (bytes(512), b"authentication-like data".ljust(8192, b"\0")):
            with self.subTest(bytes=len(suffix)), self.assertRaisesRegex(ValueError, "trailing"):
                CLFS.observe_base_log(fixture() + suffix)

    def test_counters_and_equal_base_flushed_fields_do_not_establish_quiescence(self):
        raw = fixture()
        before = bytes(raw)
        report = CLFS.observe_base_log(raw)
        self.assertEqual(before, raw)
        self.assertEqual([19, 20], [copy["dump_counter_raw"] for copy in report["copies"]])
        self.assertIsNone(report["active_copy"])
        self.assertEqual("unproven", report["persistent_quiescence"])
        self.assertFalse(report["writer_qualified"])
        self.assertEqual("0xffffffff00000000", report["copies"][0]["streams"][0]["lsn_fields_raw"]["48"])

    def test_crc_failure_in_either_copy_is_not_hidden_by_higher_counter(self):
        for offset in (2048, 33280):
            with self.subTest(offset=offset):
                raw = fixture()
                raw[offset + 200] ^= 1
                with self.assertRaisesRegex(ValueError, "CRC32"):
                    CLFS.observe_base_log(raw)

    def test_malformed_descriptors_and_block_envelopes(self):
        cases = {
            "signature": (0, 0, 0x16, 2, "signature"),
            "stream": (0, 3, 1, 1, "stream"),
            "version": (0, 16, 2, 4, "version"),
            "count-copy": (0, 6, 1, 2, "counts"),
            "record": (0, 40, 120, 4, "bounds"),
            "fixups": (0, 104, 1024, 4, "bounds"),
            "torn-signature": (0, 511, 8, 1, "fixup"),
            "first-bit": (0, 510, 0x10, 1, "fixup"),
            "last-bit": (0, 1022, 0x10, 1, "fixup"),
            "descriptors": (0, 112 + 72, 31, 4, "six-descriptor"),
            "overlap": (0, 112 + 76 + 2 * 24 + 16, 512, 4, "overlapping"),
            "descriptor-eof": (0, 112 + 76 + 2 * 24 + 16, 65536, 4, "bounds"),
            "identity": (0, 112 + 76 + 2 * 24 + 20, 3, 4, "identity"),
            "information-size": (2048, 112 + 4904, 65536, 4, "area"),
            "information-count": (2048, 112 + 292, 2, 4, "count"),
            "information-offset": (2048, 112 + 312, 0xfffffff8, 4, "outside"),
            "information-type": (2048, 112 + 4920, 0xc1fdf006, 4, "type/length"),
            "information-length": (2048, 112 + 4920 + 4, 128, 4, "type/length"),
        }
        for name, (offset, relative, value, width, message) in cases.items():
            with self.subTest(name=name):
                raw = fixture()
                size = 1024 if offset == 0 else 31232
                changed = bytearray(raw[offset:offset + size])
                put(changed, relative, value, width)
                raw[offset:offset + size] = checksum(changed)
                with self.assertRaisesRegex(ValueError, message):
                    CLFS.observe_base_log(raw)

    def test_names_cannot_run_into_slack_or_use_invalid_utf16(self):
        for invalid_unicode in (False, True):
            with self.subTest(invalid_unicode=invalid_unicode):
                raw = fixture()
                data = raw[2048:33280]
                start = 112 + 4920 + 136
                data[start:start + 16] = (b"\x00\xd8" if invalid_unicode else b"A\0") * 8
                if invalid_unicode:
                    data[start + 14:start + 16] = b"\0\0"
                raw[2048:33280] = checksum(data)
                with self.assertRaises(ValueError):
                    CLFS.observe_base_log(raw)

    def test_duplicate_streams_and_overlaps_are_refused(self):
        for overlap in (False, True):
            with self.subTest(overlap=overlap):
                raw = fixture()
                data, _, _ = CLFS.decode_block(raw[2048:33280])
                put(data, 112 + 292, 2)
                put(data, 112 + 316, 4920 if overlap else 5072)
                if not overlap:
                    put(data, 112 + 4904, 304)
                    data[112 + 5072:112 + 5072 + 152] = data[112 + 4920:112 + 4920 + 152]
                raw[2048:33280] = protect(data)
                with self.assertRaisesRegex(ValueError, "overlapping|duplicate"):
                    CLFS.observe_base_log(raw)

    def test_nonempty_reserved_descriptor_and_size_budgets_are_refused(self):
        raw = fixture()
        raw[1024] = 1
        with self.assertRaisesRegex(ValueError, "reserved"):
            CLFS.observe_base_log(raw)
        for raw in (b"", bytes(511), bytes(65535), bytes(128 * 1024 + 512)):
            with self.subTest(size=len(raw)), self.assertRaises(ValueError):
                CLFS.observe_base_log(raw)

    def test_different_copy_state_is_reported_without_selecting_an_active_copy(self):
        raw = fixture()
        data, _, _ = CLFS.decode_block(raw[33280:64512])
        put(data, 112 + 4920 + 8 + 56, 0x100, 8)
        raw[33280:64512] = protect(data)
        report = CLFS.observe_base_log(raw)
        self.assertNotEqual(report["copies"][0]["streams"], report["copies"][1]["streams"])
        self.assertIsNone(report["active_copy"])
        self.assertFalse(report["writer_qualified"])

    def test_physical_stream_path_is_not_followed_or_emitted(self):
        raw = fixture()
        for offset in (2048, 33280):
            data, _, _ = CLFS.decode_block(raw[offset:offset + 31232])
            put(data, 112 + 4920 + 8, 0, 2)
            raw[offset:offset + 31232] = protect(data)
        report = CLFS.observe_base_log(raw)
        self.assertEqual("[physical stream path omitted]", report["copies"][0]["streams"][0]["name"])

    def test_sealed_file_identity_hash_and_readonly_inspection(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "synthetic.blf"
            raw = fixture()
            path.write_bytes(raw)
            sha = hashlib.sha256(raw).hexdigest()
            before = path.stat()
            report = CLFS.inspect_file(path, sha.upper())
            self.assertEqual(sha, report["unchanged_source_sha256"])
            self.assertEqual(raw, path.read_bytes())
            self.assertEqual(before.st_mtime_ns, path.stat().st_mtime_ns)
            with self.assertRaisesRegex(ValueError, "SHA256 differs"):
                CLFS.inspect_file(path, "0" * 64)
            with self.assertRaisesRegex(ValueError, "hexadecimal"):
                CLFS.inspect_file(path, "x" * 64)
            link = pathlib.Path(directory) / "link.blf"
            link.symlink_to(path)
            with self.assertRaisesRegex(ValueError, "linked"):
                CLFS.inspect_file(link, sha)

    def test_nonregular_hardlinked_and_midinspection_changed_sources_are_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "synthetic.blf"
            raw = fixture()
            path.write_bytes(raw)
            sha = hashlib.sha256(raw).hexdigest()
            linked = pathlib.Path(directory) / "hardlink.blf"
            linked.hardlink_to(path)
            with self.assertRaisesRegex(ValueError, "ordinary"):
                CLFS.inspect_file(path, sha)
            linked.unlink()
            original = CLFS.observe_base_log

            def change_source(data):
                report = original(data)
                path.write_bytes(bytes(len(raw)))
                return report

            with mock.patch.object(CLFS, "observe_base_log", side_effect=change_source):
                with self.assertRaisesRegex(ValueError, "changed"):
                    CLFS.inspect_file(path, sha)
            if hasattr(CLFS.os, "mkfifo"):
                fifo = pathlib.Path(directory) / "fifo.blf"
                CLFS.os.mkfifo(fifo)
                with self.assertRaisesRegex(ValueError, "ordinary"):
                    CLFS.inspect_file(fifo, sha)


if __name__ == "__main__":
    unittest.main()
