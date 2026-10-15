"""Regenerate synthetic v4 golden data using Python's standard library only.

No game files or production C# code are read. The table's counter schedule is
deliberately fixed: the first 64-byte record exhausts the cache and resets the
counter; the 110-byte second record crosses a block without resetting it.
"""
import base64
import hashlib
import json
from pathlib import Path
import struct
import zlib

U32 = 0xffffffff
KEY_MASK = bytes.fromhex("7b2f3548439502b9ae91a6e1d8d624b433101d3dc1bbc6f4a5feb3696b56e475")


def chacha_block(key, nonce=b"\0" * 8, counter=0):
    initial = list(struct.unpack("<4I", b"expand 32-byte k"))
    initial += list(struct.unpack("<8I", key))
    initial += [counter & U32, counter >> 32] + list(struct.unpack("<2I", nonce))
    words = initial.copy()

    def quarter(a, b, c, d):
        for left, right, rotate in ((a, b, 16), (c, d, 12), (a, b, 8), (c, d, 7)):
            words[left] = (words[left] + words[right]) & U32
            target = d if left == a else b
            value = words[target] ^ words[left]
            words[target] = ((value << rotate) | (value >> (32 - rotate))) & U32

    for _ in range(10):
        for indices in ((0, 4, 8, 12), (1, 5, 9, 13), (2, 6, 10, 14), (3, 7, 11, 15),
                        (0, 5, 10, 15), (1, 6, 11, 12), (2, 7, 8, 13), (3, 4, 9, 14)):
            quarter(*indices)
    return struct.pack("<16I", *((a + b) & U32 for a, b in zip(initial, words)))


def xor_bytes(left, right):
    return bytes(a ^ b for a, b in zip(left, right))


def package_key(name, secret, table=False):
    text = name.lower() + secret
    values = ((i % 3 + 2) * ord(text[-1 - i % len(text)]) if table else ord(text[i % len(text)])
              for i in range(32))
    return bytes(((value + i) & 255) ^ KEY_MASK[i] for i, value in enumerate(values))


def payload_key(path, digest):
    return bytes((((digest[i % 16] + 2 + i % 5) * ord(path[i % len(path)]) + i) & 255) ^ KEY_MASK[i]
                 for i in range(32))


def payload_nonce(secret):
    value = 0x811c9dc5
    for char in secret:
        value = ((value ^ ord(char)) * 0x1000193) & U32
    original = struct.pack("<III", value, value >> 1, (value >> 1) ^ 0x6c)
    transform = bytes(((((17 * i) ^ (8 * i)) + ((i >> 2) ^ (0x5a + 0x2b * i))
                        + 11 * (i // 11) - i) & 255) for i in range(12))
    result = xor_bytes(original, transform)
    return result[:8], struct.unpack("<I", result[8:])[0]


def offsets(name):
    total = sum(map(ord, name.lower()))
    header = total % 312 + 30
    return header, header + 39 + (total * 3) % 212 + 64


def generate_v2(fixture_dir):
    # Fixed legacy keystream captured once from the unchanged upstream
    # HFSSerpent implementation for this exact synthetic filename. These
    # constants make regeneration independent of the production library.
    # V2 tests cover framing/decompression/version detection, not a new
    # independent validation of that legacy cipher algorithm.
    name = "0" * 40 + "_00000.hfs"
    header_stream = bytes.fromhex("F36FF5C0C05D7662B7DA5317")
    table_stream = bytes.fromhex(
        "D74F70F07BB1E4A28E6486AE2E30721B8F9DF449D33D8F880223424BBF7BD64D"
        "1F9BD58BCA4ECF5132929F8F7BA20C5F5D3195858C1C13414E0150A1FEA35D60"
        "95E2C58FFE165D7CAA332E9C6E4B0BD462A138592D687CB56732965352D23FAE"
        "67A43A62C0B11394662BBFF18883CBF80B06891A8B9DCCB073E79533C56C80CE"
        "A65EFE83C2C0D7CD848A2D8F6D6DE51F")

    def encode_words(plain, stream):
        words = [((value + key) & U32) for (value,), (key,) in
                 zip(struct.iter_unpack("<I", plain), struct.iter_unpack("<I", stream))]
        return struct.pack(f"<{len(words)}I", *words)

    total = sum(map(ord, name))
    header_offset = total % 312 + 30
    table_offset = header_offset + 9 + (3 * total) % 212 + 33
    table = bytearray()
    entries = []
    payloads = []
    for index, (path, plain, flags) in enumerate([
        ("u.tx", b"Legacy v2 raw fixture.\n", 0),
        ("z.tx", b"Legacy v2 compressed fixture.\n" * 40, 1),
        ("e.tx", b"", 0),
    ]):
        stored = zlib.compress(plain, 9) if flags & 1 else plain
        record_offset = len(table)
        table += struct.pack("<i", len(path)) + path.encode("utf-16le")
        table += struct.pack("<IIiii", 0, flags, index, len(plain), len(stored))
        table += hashlib.md5(plain).digest()
        entries.append(dict(path=path, flags=flags, start_block=index, size=len(plain), stored=len(stored),
                            record_offset=record_offset, expected_base64=base64.b64encode(plain).decode(),
                            sha256=hashlib.sha256(plain).hexdigest()))
        payloads.append(stored)
    assert len(table) == len(table_stream) == 144
    data_offset = ((table_offset + len(table) + 1023) // 1024) * 1024
    archive = bytearray(data_offset + 2 * 1024)
    # The encrypted v2 header begins with 0xf8. Signed prefix 0xf8 >> 1
    # XOR that byte produces 4, colliding with the v4 discriminator.
    archive[0] = 0xf8
    header = struct.pack("<iBi", 5, 2, 3) + b"\0" * 3
    archive[header_offset:header_offset + 12] = encode_words(header, header_stream)
    assert ((archive[header_offset] ^ (-8 >> 1)) & 255) == 4
    archive[table_offset:table_offset + len(table)] = encode_words(table, table_stream)
    for index, stored in enumerate(payloads):
        start = data_offset + index * 1024
        archive[start:start + len(stored)] = stored
    (fixture_dir / "synthetic-v2.bin").write_bytes(archive)
    manifest = dict(filename=name, header_offset=header_offset, table_offset=table_offset, data_offset=data_offset,
                    table_logical_size=144, table_physical_size=144, table_counters=[],
                    archive_sha256=hashlib.sha256(archive).hexdigest(), entries=entries)
    (fixture_dir / "synthetic-v2.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Generated synthetic v2 collision fixture: {len(archive)} bytes, {len(entries)} entries")


def main():
    # RFC 8439 section 2.3.2, represented using the original 64-bit nonce layout.
    assert chacha_block(bytes(range(32)), bytes.fromhex("0000004a00000000"), 0x0900000000000001).hex() == (
        "10f1e7e4d13b5915500fdd1fa32071c4c7d1f4c733c068030422aa9ac3d46c4e"
        "d2826446079faa0914c2d705d98b02a2b5129cd1de164eb9cbd083e8a2503c4e")

    # Force physical table buffering across a 1024-byte boundary while its
    # logical end stays below that boundary. A logical-position parser fails.
    for candidate in range(100000):
        name = f"{candidate:040x}_00000.hfs"
        header_offset, table_offset = offsets(name)
        if 513 <= table_offset <= 530:
            break
    else:
        raise AssertionError("Could not find the required synthetic filename")

    secret = "synthetic-key"
    assert len(secret) == 13
    raw_entries = [
        ("a.tx", b"Synthetic unencrypted HFS v4 payload.\n", 0),
        ("nested/" + "x" * 16 + ".txt", b"Independent compressed fixture.\n" * 160, 7),
        ("b.tx", bytes((i * 29 + i // 7) & 255 for i in range(2500)), 4),
        ("c.tx", b"", 1),
        ("d.tx", b"", 0),
        ("e.tx", b"Flag 0x02 has no payload transform in v4.\n", 2),
        ("f.tx", b"", 7),
    ]
    table = bytearray()
    payloads = []
    entries = []
    next_block = 0
    nonce, initial_counter = payload_nonce(secret)
    for path, plain, flags in raw_entries:
        digest = hashlib.md5(plain).digest()
        stored = zlib.compress(plain, 9) if flags & 1 else plain
        if path == "b.tx":
            stored += b"padding-not-part-of-file"
        if flags & 4:
            data = bytearray(stored)
            for offset in range(0, min(1024, len(data)), 64):
                count = min(64, len(data) - offset)
                data[offset:offset + count] = xor_bytes(data[offset:offset + count],
                    chacha_block(payload_key(path, digest), nonce, initial_counter + offset // 64))
            stored = bytes(data)
        record_offset = len(table)
        path_bytes = path.encode("utf-16le")
        table += struct.pack("<i", len(path)) + path_bytes
        table += struct.pack("<IIiiiII", 0, flags, next_block, len(plain), len(stored), 0, 0)
        table += digest + struct.pack("<II", 0, 0)
        entries.append(dict(path=path, flags=flags, start_block=next_block, size=len(plain), stored=len(stored),
                            record_offset=record_offset, expected_base64=base64.b64encode(plain).decode(),
                            sha256=hashlib.sha256(plain).hexdigest()))
        payloads.append((next_block, stored))
        next_block += (len(stored) + 1023) // 1024

    assert len(table) == 494
    physical_size = 512
    table.extend(b"\0" * (physical_size - len(table)))
    # This known counter sequence is independent of the production table reader.
    counters = [0, 0, 1, 2, 3, 4, 5, 6]
    table_ciphertext = b"".join(xor_bytes(table[i * 64:(i + 1) * 64],
        chacha_block(package_key(name, secret, True), counter=counter)) for i, counter in enumerate(counters))
    data_offset = ((table_offset + physical_size + 1023) // 1024) * 1024
    assert table_offset + 494 <= 1024 < table_offset + physical_size

    prefix = bytes((i * 37 + 0xa3) & 255 for i in range(header_offset))
    signed = lambda value: value if value < 128 else value - 256
    encoded_version = (4 ^ (signed(prefix[0]) >> 1)) & 255
    encoded_length = (13 ^ (signed(prefix[0]) >> 1)) & U32
    encoded_secret = [(ord(char) ^ (signed(prefix[i]) >> 1) ^ 0x4b) & 65535 for i, char in enumerate(secret)]
    checksum = (encoded_length + encoded_version + 4 + sum(encoded_secret) + len(entries)) & U32
    extension = struct.pack("<Ii", checksum, len(entries))
    header = bytes([encoded_version]) + struct.pack("<I13H", encoded_length, *encoded_secret)
    header += xor_bytes(extension, chacha_block(package_key(name, secret)))
    archive = bytearray(data_offset + max(block * 1024 + len(data) for block, data in payloads))
    archive[:len(prefix)] = prefix
    archive[header_offset:header_offset + len(header)] = header
    archive[table_offset:table_offset + physical_size] = table_ciphertext
    for block, data in payloads:
        start = data_offset + block * 1024
        archive[start:start + len(data)] = data

    fixture_dir = Path(__file__).with_name("Fixtures")
    fixture_dir.mkdir(exist_ok=True)
    (fixture_dir / "synthetic-v4.bin").write_bytes(archive)
    manifest = dict(filename=name, header_offset=header_offset, table_offset=table_offset, data_offset=data_offset,
                    table_logical_size=494, table_physical_size=physical_size, table_counters=counters,
                    archive_sha256=hashlib.sha256(archive).hexdigest(), entries=entries)
    (fixture_dir / "synthetic-v4.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Generated synthetic v4 fixture: {len(archive)} bytes, {len(entries)} entries, {name}")
    generate_v2(fixture_dir)


if __name__ == "__main__":
    main()
