#!/usr/bin/env python3
"""Bounded read-only observations of a generated Windows TxF base log.

This is investigation tooling, not a CLFS recovery engine or a writer gate.
Only the observed six-descriptor base log and unrecycled container prefix are
decoded. Neither the
highest dump counter nor equal stream LSNs establishes persistent quiescence.
No log, container, path named inside a log, or host volume is ever opened for
writing. See docs/dependency-evaluation.md for provenance and limitations.
"""
import argparse
import hashlib
import json
import os
import pathlib
import stat
import uuid
import zlib


MAX_BASE_BYTES = 128 * 1024
MAX_CONTAINER_BYTES = 2 * 1024 * 1024
MAX_CONTAINER_BLOCKS = 256
MAX_CONTAINER_RECORDS = 1024
HEADER_BYTES = 112
REFERENCE = (
    "https://github.com/libyal/libfsclfs/blob/"
    "5966065b505f70a2b4439ad8e7ebc546495ee87f/documentation/"
    "Common%20Log%20File%20System%20(CLFS).asciidoc"
)


def field(data, offset, size):
    if offset < 0 or size < 0 or offset + size > len(data):
        raise ValueError("field outside bounded CLFS structure")
    return int.from_bytes(data[offset:offset + size], "little")


def decode_block(raw):
    """Check only the observed base-block envelope; retain all unknown bytes."""
    if not HEADER_BYTES <= len(raw) <= MAX_BASE_BYTES or len(raw) % 512:
        raise ValueError("CLFS block size outside aligned inspection budget")
    count = field(raw, 4, 2)
    if (field(raw, 0, 2) != 0x15 or raw[3] != 0
            or field(raw, 16, 4) != 1):
        raise ValueError("unsupported CLFS base-block signature/stream/version")
    if count * 512 != len(raw) or field(raw, 6, 2) != count:
        raise ValueError("CLFS sector counts disagree")
    record, fixups = field(raw, 40, 4), field(raw, 104, 4)
    if (record != HEADER_BYTES or fixups % 8 or fixups < record
            or fixups + count * 2 > len(raw) - 2):
        raise ValueError("unsupported CLFS record/fixup bounds")
    checksum_bytes = bytearray(raw)
    checksum_bytes[12:16] = bytes(4)
    # Empirically matches all four blocks of the retained Windows fixture.
    # This byte-consistency check is NOT a documented recovery-state test.
    if zlib.crc32(checksum_bytes) != field(raw, 12, 4):
        raise ValueError("CLFS observed CRC32 mismatch")
    decoded = bytearray(raw)
    for sector in range(count):
        tail = (sector + 1) * 512 - 2
        expected_low = 0x10 | (0x40 if sector == 0 else 0) | (0x20 if sector == count - 1 else 0)
        if raw[tail] != expected_low or raw[tail + 1] != raw[2]:
            raise ValueError("CLFS sector signature/fixup mismatch")
        decoded[tail:tail + 2] = raw[fixups + sector * 2:fixups + sector * 2 + 2]
    return decoded, record, fixups


def bounded_name(data, start, end):
    if start % 2 or start < 0 or start >= end or end > len(data):
        raise ValueError("CLFS name outside information area")
    for position in range(start, min(end - 1, start + 1026), 2):
        if data[position:position + 2] == b"\0\0":
            return bytes(data[start:position]).decode("utf-16-le", errors="strict")
    raise ValueError("CLFS name lacks bounded UTF-16 terminator")


def metadata_copy(raw, physical_offset):
    data, record, fixups = decode_block(raw)
    if record + 4920 > fixups:
        raise ValueError("CLFS metadata header overlaps fixups")
    information_start = record + 4920
    information_end = information_start + field(data, record + 4904, 4)
    if information_end > fixups:
        raise ValueError("CLFS information area exceeds metadata block")
    occupied = []

    def records(array_offset, slots, count_offset, kind, length):
        offsets = [field(data, record + array_offset + index * 4, 4) for index in range(slots)]
        offsets = [offset for offset in offsets if offset]
        if len(offsets) != field(data, record + count_offset, 4):
            raise ValueError("CLFS information count disagrees with offset array")
        for relative in offsets:
            # The Windows fixture's array offsets are relative to the metadata
            # record, despite the older research table's block-relative label.
            position = record + relative
            if (relative % 8 or position < information_start
                    or position + length > information_end):
                raise ValueError("CLFS information record outside bounded area")
            if field(data, position, 4) != kind or field(data, position + 4, 4) != length:
                raise ValueError("unsupported CLFS information record type/length")
            name = bounded_name(data, position + length, information_end)
            end = position + length + len(name.encode("utf-16-le")) + 2
            if any(position < old_end and old_start < end for old_start, old_end in occupied):
                raise ValueError("overlapping CLFS information records/names")
            occupied.append((position, end))
            yield position, name

    streams = []
    for position, name in records(312, 124, 292, 0xc1fdf007, 136):
        start = position + 8
        number = field(data, start, 2)
        streams.append({
            "stream_number": number,
            # Do not propagate the physical stream's host-device path into
            # portable evidence. Names are data, never paths to follow.
            "name": name if number else "[physical stream path omitted]",
            "flags_raw": hex(field(data, start + 2, 2)),
            "lsn_fields_raw": {
                str(offset): f"0x{field(data, start + offset, 8):016x}"
                for offset in range(48, 96, 8)
            },
        })
    if len({stream["stream_number"] for stream in streams}) != len(streams):
        raise ValueError("duplicate CLFS stream numbers")
    containers = []
    for position, _ in records(808, 1024, 300, 0xc1fdf008, 48):
        start = position + 8
        containers.append({
            "bytes": field(data, start, 4),
            "physical_number": field(data, start + 8, 4),
            "logical_number": field(data, start + 12, 4),
            "stream_count_raw": field(data, start + 24, 4),
        })
    return {
        "physical_offset": physical_offset,
        "block_sha256": hashlib.sha256(raw).hexdigest(),
        "observed_crc32_matches": True,
        "sector_signatures_match": True,
        "dump_counter_raw": field(data, record, 4),
        "store_guid": str(uuid.UUID(bytes_le=bytes(data[record + 8:record + 24]))),
        "streams": streams,
        "containers": containers,
    }


def observe_base_log(raw):
    if not 1024 <= len(raw) <= MAX_BASE_BYTES or len(raw) % 512:
        raise ValueError("CLFS base log exceeds aligned inspection budget")
    first_bytes = field(raw, 4, 2) * 512
    first, record, fixups = decode_block(raw[:first_bytes])
    if field(first, record + 72, 4) != 6 or record + 76 + 6 * 24 > fixups:
        raise ValueError("only the observed six-descriptor CLFS layout is supported")
    descriptors = []
    for index in range(6):
        position = record + 76 + index * 24
        size, offset, identity = [field(first, position + displacement, 4) for displacement in (12, 16, 20)]
        if (identity != index or not size or size % 512 or offset % 512
                or size > MAX_BASE_BYTES or offset + size > len(raw)):
            raise ValueError("CLFS descriptor identity/bounds disagree")
        if any(offset < old[0] + old[1] and old[0] < offset + size for old in descriptors):
            raise ValueError("overlapping CLFS block descriptors")
        descriptors.append((offset, size))
    if descriptors[0] != (0, first_bytes):
        raise ValueError("CLFS first descriptor does not identify its own block")
    ordered = sorted(descriptors)
    if (ordered[0][0] != 0 or ordered[-1][0] + ordered[-1][1] != len(raw)
            or any(left[0] + left[1] != right[0] for left, right in zip(ordered, ordered[1:]))):
        raise ValueError("unqualified CLFS descriptor gaps/trailing data, including authentication suffixes")
    for index in (1, 5):
        offset, size = descriptors[index]
        if any(raw[offset:offset + size]):
            raise ValueError("unsupported nonempty CLFS reserved descriptor")
    offset, size = descriptors[4]
    decode_block(raw[offset:offset + size])
    copies = [metadata_copy(raw[offset:offset + size], offset) for offset, size in descriptors[2:4]]
    return {
        "schema": 1,
        "scope": "Read-only structural observations of generated TxF CLFS base metadata",
        "reference": REFERENCE,
        "base_bytes": len(raw),
        "base_sha256": hashlib.sha256(raw).hexdigest(),
        "copies": copies,
        "active_copy": None,
        "persistent_quiescence": "unproven",
        "writer_qualified": False,
        "limitations": [
            "CRC32, fixups and dump counters are not transaction completion evidence.",
            "LSN fields are raw observations; sentinel and active-range semantics are unqualified.",
            "Container owner pages, KTM/TxF restart/recovery and prepared/indoubt state are not decoded.",
            "TOPS/$TXF_DATA update obligations and LFS 2.0 cache safety remain separate blockers.",
        ],
    }


def observe_container(raw):
    """Observe the generated, unrecycled container-0 prefix, never live ranges.

    This deliberately excludes owner pages, split blocks, recycled containers,
    continuation assembly and payload interpretation. Physical scanning cannot
    establish which historical records a client would use during recovery.
    """
    if len(raw) != MAX_CONTAINER_BYTES:
        raise ValueError("only generated 2 MiB containers are in this observation budget")
    blocks, seen_lsns, previous_lsns, next_virtual, restarts = [], set(), {}, {}, {}
    offset, record_count = 0, 0
    while offset < len(raw):
        if not any(raw[offset:offset + 512]):
            if any(raw[offset:]):
                raise ValueError("nonzero data after container prefix; owner/recycled layout unsupported")
            break
        if len(blocks) >= MAX_CONTAINER_BLOCKS:
            raise ValueError("container block count exceeds observation budget")
        sectors = field(raw, offset + 4, 2)
        size = sectors * 512
        # This observed prefix never reaches an owner page. Do not scan past
        # one, reconstruct mappings heuristically, or accept a split block.
        if not sectors or offset + size > min(512 * 1024 - 4096, len(raw)):
            raise ValueError("container block exceeds prefix/owner-page boundary")
        encoded = raw[offset:offset + size]
        stream = encoded[3]
        if (field(encoded, 0, 2) != 0x15 or stream not in (1, 2)
                or field(encoded, 6, 2) != sectors or field(encoded, 16, 4) != 1):
            raise ValueError("unsupported container signature/stream/count/version")
        if field(encoded, 12, 4) != 0:
            raise ValueError("nonzero container checksum semantics unsupported")
        if field(encoded, 24, 8) != offset:
            raise ValueError("only unrecycled logical-container-0 physical LSNs supported")
        record, fixups = field(encoded, 40, 4), field(encoded, 104, 4)
        if (record != HEADER_BYTES or fixups % 8 or fixups < HEADER_BYTES + 40
                or fixups + sectors * 2 > len(encoded) - 2):
            raise ValueError("container record/fixup bounds invalid")
        if field(encoded, 44, 4) or field(encoded, 48, 4):
            raise ValueError("owner-page mappings unsupported")
        data = bytearray(encoded)
        for sector in range(sectors):
            tail = (sector + 1) * 512 - 2
            expected = 4 | (0x40 if sector == 0 else 0) | (0x20 if sector == sectors - 1 else 0)
            if encoded[tail:tail + 2] != bytes((expected, encoded[2])):
                raise ValueError("container sector signature/fixup mismatch")
            data[tail:tail + 2] = encoded[fixups + sector * 2:fixups + sector * 2 + 2]
        records, position, terminated = [], record, False
        virtual = next_virtual.get(stream, field(data, record, 8))
        if virtual % 512 or virtual > offset:
            raise ValueError("initial container virtual LSN exceeds observed physical prefix")
        while position + 40 <= fixups:
            if record_count >= MAX_CONTAINER_RECORDS or len(records) >= 512:
                raise ValueError("container record count exceeds observation budget")
            lsn, undo, previous = [field(data, position + relative, 8) for relative in (0, 8, 16)]
            length, unknown = field(data, position + 24, 4), field(data, position + 28, 4)
            flags, payload_offset, kind = [field(data, position + relative, width)
                                           for relative, width in ((32, 2), (34, 2), (36, 4))]
            if (length < 40 or payload_offset != 40 or position + length > fixups
                    or flags or unknown or kind not in (1, 5, 0x21, 0x22, 0x25, 0x26)):
                raise ValueError("unsupported container record bounds/flags/continuation")
            if lsn != virtual + len(records) or (stream, lsn) in seen_lsns:
                raise ValueError("container virtual LSN sequence/record identity mismatch")
            if stream in previous_lsns and lsn <= previous_lsns[stream]:
                raise ValueError("non-increasing container stream LSNs")
            seen_lsns.add((stream, lsn))
            previous_lsns[stream] = lsn
            payload = bytes(data[position + payload_offset:position + length])
            observation = {
                "offset_in_block": position, "lsn_raw": f"0x{lsn:016x}",
                "undo_next_lsn_raw": f"0x{undo:016x}", "previous_lsn_raw": f"0x{previous:016x}",
                "record_type_raw": hex(kind), "restart_type_bit": bool(kind & 2),
                "bytes": length, "payload_bytes": len(payload),
                "payload_sha256": hashlib.sha256(payload).hexdigest(),
                "payload_hex": payload.hex(),
            }
            records.append(observation)
            record_count += 1
            if kind & 2:
                restarts[stream] = observation
            aligned_end = (position + length + 7) & ~7
            if aligned_end > fixups or any(data[position + length:aligned_end]):
                raise ValueError("container record alignment/padding invalid")
            position = aligned_end
            if kind & 0x20:
                terminated = True
                break
        if not terminated or any(data[position:fixups]):
            raise ValueError("missing container last-record marker or nonzero record slack")
        blocks.append({
            "physical_offset": offset, "bytes": size, "stream_number": stream,
            "sector_signatures_match": True, "checksum_validated": False,
            "next_block_lsn_raw": f"0x{field(encoded, 32, 8):016x}",
            "block_sha256": hashlib.sha256(encoded).hexdigest(), "records": records,
        })
        next_virtual[stream] = offset + size
        offset += size
    return {
        "schema": 1, "scope": "Generated CLFS container prefix observations, not recovery qualification",
        "container_bytes": len(raw), "container_sha256": hashlib.sha256(raw).hexdigest(),
        "occupied_prefix_bytes": offset, "blocks": blocks, "record_count": record_count,
        "last_physical_restart_candidates": {str(key): value for key, value in restarts.items()},
        "persistent_quiescence": "unproven", "writer_qualified": False,
        "limitations": [
            "Physical order and restart type bits do not select live recovery ranges or an active base copy.",
            "Owner pages, recycled/split blocks and continuation records are unsupported.",
            "Zero container checksum has no qualified integrity meaning; sector signatures are not a checksum.",
            "Client payloads are opaque; TxF/KTM checkpoint tables and prepared/indoubt state remain unqualified.",
        ],
    }


def inspect_file(path, expected_sha256, *, container=False):
    if len(expected_sha256) != 64 or any(c not in "0123456789abcdefABCDEF" for c in expected_sha256):
        raise ValueError("expected SHA256 must have exactly 64 hexadecimal digits")
    path = pathlib.Path(os.path.abspath(path))
    for part in (path, *path.parents):
        if part.is_symlink():
            raise ValueError("linked CLFS inspection paths are forbidden")
    budget = MAX_CONTAINER_BYTES if container else MAX_BASE_BYTES
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    with os.fdopen(os.open(path, flags), "rb") as source:
        before = os.fstat(source.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > budget:
            raise ValueError("expected bounded ordinary generated CLFS file")
        raw = source.read(budget + 1)
        digest = hashlib.sha256(raw).hexdigest()
        if digest != expected_sha256.lower():
            raise ValueError("CLFS input SHA256 differs from sealed extraction evidence")
        report = observe_container(raw) if container else observe_base_log(raw)
        source.seek(0)
        after_digest = hashlib.sha256(source.read(budget + 1)).hexdigest()
        after, current = os.fstat(source.fileno()), path.stat()
        identity = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns, s.st_nlink)
        if identity(before) != identity(after) or identity(after) != identity(current) or digest != after_digest:
            raise ValueError("CLFS source changed during inspection")
        report["unchanged_source_sha256"] = after_digest
        return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base_log", type=pathlib.Path)
    parser.add_argument("--container", action="store_true", help="observe only the generated unrecycled 2 MiB container prefix")
    parser.add_argument("--sha256", required=True, help="independently recorded generated stream SHA256")
    args = parser.parse_args()
    try:
        report = inspect_file(args.base_log, args.sha256, container=args.container)
    except (OSError, ValueError) as error:
        parser.exit(2, f"CLFS observation refused: {error}\n")
    print(json.dumps(report, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
