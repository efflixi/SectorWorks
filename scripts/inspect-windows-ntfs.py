#!/usr/bin/env python3
"""Read-only inventory of the generated, detached <=160 MiB NTFS partition.

Reports observations, not filesystem or writer qualification.
Never mounts, modifies, repairs or normalizes the input.
"""
import argparse
import hashlib
import json
import pathlib
import struct


def number(data, offset, size):
    if offset < 0 or offset + size > len(data):
        raise ValueError("field outside bounded structure")
    return int.from_bytes(data[offset:offset + size], "little")


def fixed_record(raw):
    data = bytearray(raw)
    usa, count = number(data, 4, 2), number(data, 6, 2)
    if count != len(data) // 512 + 1 or usa + count * 2 > len(data):
        raise ValueError("invalid fixup geometry")
    sequence = data[usa:usa + 2]
    for index in range(1, count):
        end = index * 512 - 2
        if data[end:end + 2] != sequence:
            raise ValueError("invalid fixup sequence")
        data[end:end + 2] = data[usa + index * 2:usa + index * 2 + 2]
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("partition", type=pathlib.Path)
    args = parser.parse_args()
    if args.partition.is_symlink() or not args.partition.is_file():
        raise ValueError("expected ordinary generated partition file")
    size = args.partition.stat().st_size
    if not 1024 * 1024 <= size <= 160 * 1024 * 1024:
        raise ValueError("generated partition size outside inspection budget")
    with args.partition.open("rb") as image:
        boot = image.read(512)
        if boot[3:11] != b"NTFS    " or number(boot, 11, 2) != 512:
            raise ValueError("expected 512-byte-sector NTFS")
        cluster = 512 * boot[13]
        encoded_size = struct.unpack_from("b", boot, 64)[0]
        record_size = 1 << -encoded_size if encoded_size < 0 else encoded_size * cluster
        if record_size not in (512, 1024, 2048, 4096):
            raise ValueError("inspection record-size budget exceeded")
        # The known generated fixture has a 256 KiB initialized MFT. Refuse
        # larger input instead of using this observation tool as a general parser.
        image.seek(number(boot, 48, 8) * cluster)
        first = fixed_record(image.read(record_size))
        position = number(first, 20, 2)
        for _ in range(128):
            kind, length = number(first, position, 4), number(first, position + 4, 4)
            if kind == 0xffffffff or length < 24 or position + length > len(first):
                raise ValueError("missing ordinary MFT data mapping")
            if kind == 0x80 and first[position + 9] == 0:
                break
            position += length
        else:
            raise ValueError("MFT bootstrap attribute budget exceeded")
        mapping = first[position:position + length]
        if mapping[8] != 1 or number(mapping, 16, 8) != 0:
            raise ValueError("expected ordinary nonresident MFT mapping")
        cursor = number(mapping, 32, 2)
        header = number(mapping, cursor, 1)
        count_size, delta_size = header & 15, header >> 4
        if not 1 <= count_size <= 8 or not 1 <= delta_size <= 8:
            raise ValueError("unsupported generated MFT run widths")
        count = number(mapping, cursor + 1, count_size)
        lcn_start = cursor + 1 + count_size
        lcn = int.from_bytes(mapping[lcn_start:lcn_start + delta_size], "little", signed=True)
        if number(mapping, lcn_start + delta_size, 1) != 0:
            raise ValueError("inspection requires the generated single-run MFT")
        initialized = number(mapping, 56, 8)
        if (lcn != number(boot, 48, 8) or initialized > 1024 * 1024
                or initialized > count * cluster or lcn * cluster + initialized > size):
            raise ValueError("generated MFT bootstrap bounds disagree")
        image.seek(lcn * cluster)
        mft = image.read(initialized)
        if len(mft) > 1024 * 1024 or len(mft) % record_size:
            raise ValueError("generated MFT inventory budget exceeded")
        records = []
        for offset in range(0, len(mft), record_size):
            raw = mft[offset:offset + record_size]
            if raw[:4] != b"FILE":
                continue
            data = fixed_record(raw)
            flags = number(data, 22, 2)
            if not flags & 1:
                continue
            record = {
                "id": offset // record_size, "record_flags": hex(flags),
                "sequence": number(data, 16, 2), "base_reference": number(data, 32, 8),
                "record_sha256": hashlib.sha256(raw).hexdigest(),
                "names": [], "attributes": [],
            }
            position, used = number(data, 20, 2), number(data, 24, 4)
            if used > len(data):
                raise ValueError("record exceeds fixed structure")
            for _ in range(128):
                kind = number(data, position, 4)
                if kind == 0xffffffff:
                    break
                length = number(data, position + 4, 4)
                if length < 24 or position + length > used:
                    raise ValueError("attribute outside record")
                attribute = data[position:position + length]
                nonresident = attribute[8]
                name_length, name_offset = attribute[9], number(attribute, 10, 2)
                name = bytes(attribute[name_offset:name_offset + name_length * 2]).decode("utf-16-le")
                item = {
                    "type": hex(kind), "name": name, "nonresident": bool(nonresident),
                    "flags": hex(number(attribute, 12, 2)),
                    "attribute_id": number(attribute, 14, 2),
                    "attribute_sha256": hashlib.sha256(attribute).hexdigest(),
                }
                if not nonresident:
                    start, count = number(attribute, 20, 2), number(attribute, 16, 4)
                    if start + count > len(attribute):
                        raise ValueError("resident value outside attribute")
                    value = attribute[start:start + count]
                    item.update(data_bytes=count, data_sha256=hashlib.sha256(value).hexdigest())
                    if kind == 0x100 or (kind == 0x80 and 24 <= record["id"] <= 35):
                        item["bounded_metadata_value_hex"] = bytes(value[:512]).hex()
                        item["metadata_value_prefix_only"] = count > 512
                    if kind == 0x10 and count >= 72:
                        record["security_id"] = number(value, 52, 4)
                        record["file_flags"] = hex(number(value, 32, 4))
                    if kind == 0x30 and count >= 66:
                        record["names"].append({
                            "parent_reference": number(value, 0, 8),
                            "namespace": value[65],
                            "name": bytes(value[66:66 + value[64] * 2]).decode("utf-16-le"),
                        })
                    if kind == 0x90 and count >= 32:
                        item["index_header_flags"] = value[28]
                        item["index_entries_bytes"] = number(value, 20, 4)
                else:
                    item.update(
                        lowest_vcn=number(attribute, 16, 8), highest_vcn=number(attribute, 24, 8),
                        allocated_bytes=number(attribute, 40, 8), data_bytes=number(attribute, 48, 8),
                        initialized_bytes=number(attribute, 56, 8), runs=[])
                    cursor, lcn, logical_bytes = number(attribute, 32, 2), 0, 0
                    content, allocation = hashlib.sha256(), hashlib.sha256()
                    metadata_prefix = bytearray()
                    zeros = bytes(1024 * 1024)
                    for _ in range(4096):
                        header = number(attribute, cursor, 1)
                        cursor += 1
                        if not header:
                            break
                        count_size, delta_size = header & 15, header >> 4
                        if not 1 <= count_size <= 8 or delta_size > 8:
                            raise ValueError("invalid run widths")
                        count = number(attribute, cursor, count_size)
                        cursor += count_size
                        delta = attribute[cursor:cursor + delta_size]
                        if len(delta) != delta_size or not count:
                            raise ValueError("invalid run extent")
                        cursor += delta_size
                        lcn += int.from_bytes(delta, "little", signed=True) if delta_size else 0
                        run_bytes = count * cluster
                        sparse = delta_size == 0
                        if not sparse and (lcn < 0 or lcn * cluster + run_bytes > size):
                            raise ValueError("run outside generated partition")
                        item["runs"].append({"lcn": None if sparse else lcn, "clusters": count})
                        # Sparse $BadClus can span the whole volume, so bound
                        # logical work by the partition-size budget too.
                        if logical_bytes + run_bytes > size + cluster:
                            raise ValueError("logical stream inspection budget exceeded")
                        if not sparse:
                            image.seek(lcn * cluster)
                        remaining = run_bytes
                        while remaining:
                            amount = min(remaining, len(zeros))
                            chunk = zeros[:amount] if sparse else image.read(amount)
                            if len(chunk) != amount:
                                raise ValueError("truncated physical extent")
                            allocation.update(chunk)
                            useful = min(amount, max(0, item["data_bytes"] - logical_bytes))
                            content.update(chunk[:useful])
                            if 24 <= record["id"] <= 35 and len(metadata_prefix) < 128:
                                metadata_prefix.extend(chunk[:min(useful, 128 - len(metadata_prefix))])
                            logical_bytes += amount
                            remaining -= amount
                    else:
                        raise ValueError("run traversal budget exceeded")
                    item.update(data_sha256=content.hexdigest(), allocation_sha256=allocation.hexdigest())
                    if metadata_prefix:
                        item["bounded_metadata_prefix_hex"] = metadata_prefix.hex()
                record["attributes"].append(item)
                position += length
            else:
                raise ValueError("attribute traversal budget exceeded")
            records.append(record)
        print(json.dumps({
            "schema": 1, "scope": "Generated Windows fixture observations; not qualification",
            "partition_bytes": size, "sector_bytes": 512, "cluster_bytes": cluster,
            "record_bytes": record_size, "mft_bytes": len(mft), "records": records,
        }, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
