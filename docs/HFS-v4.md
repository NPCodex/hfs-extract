# Verified HFS v4 format — Taiwan client, 2026-10-15

These notes are backed by local packages, static analysis of the shipped reader, and an independent Python decoder. The game installation was read only. The reader DLL's SHA-256 was `2e58616e9856934431111904958fa9573ecaf09af4baee620a1d50c662f34ab7`.

## Common header

Let `name` be the lowercase archive basename including `.hfs`. `H = sum(name code units) % 312 + 30`.

- `version = uint8(file[H] XOR (int8(file[0]) >> 1))`.
- `N = int32LE(file[H+1:H+5]) XOR (int8(file[0]) >> 1)`.
- `secret[i] = int16LE(file[H+5+2*i:H+7+2*i]) XOR (int8(file[i]) >> 1) XOR 0x4B`.
- Every installed archive has version 4 and N = 13. 7,387 archives use `MBHEROES!@0u9`; many other secrets occur and must be decoded per archive.

The 8-byte encrypted extension starts at `H+5+2*N` (H+31). It contains two uint32LE values: checksum and entry count. The checksum uses **the encoded raw on-disk values**, not decoded secret text:

`checksum = unchecked_uint32(raw_length + sum(raw_uint16_secret) + raw_uint8_version + 4 + count)`.

## ChaCha20 keys and extension/table positions

All keys below are 32 bytes and XORed with this mask, shown as four little-endian uint64 words:

`B902954348352F7B B424D6D8E1A691AE F4C6BBC13D1D1033 75E4566B69B3FEA5`.

Let `combo = lowercase archive basename + secret` and `L = len(combo)`.

- Header key byte i: `(combo[i % L] + i) & 255`.
- Table key byte i: `(combo[L - 1 - i % L] * (i % 3 + 2) + i) & 255`.
- Header and table use the original ChaCha20 layout with an 8-byte zero nonce and 64-bit counter starting at zero.
- Table physical offset is `H + 5 + 2*N + 8 + (3*sum(name) % 212) + 64`.

## Table stream behavior

The table stream buffers complete 64-byte ChaCha blocks. A read fills `ceil(remainingRequested/64)` blocks when its buffer is empty, starting with the current counter, then advances the counter by fetched block count.

A critical compatibility behavior: a request that **exactly** consumes the available buffer resets the counter to zero. A request larger than the buffer consumes the available bytes and continues filling without resetting. A partial consume leaves both buffer remainder and counter intact. Consequently parser reads cannot be combined freely.

The native read sequence per entry is:

1. uint32 path length (one 4-byte read), UTF16 path (one `length*2` byte read).
2. Seven separate uint32 reads: checksum, flags, start block, original size, stored size, reserved, block hash count.
3. Each block hash (16 bytes per read), primary hash (16 bytes).
4. If flags & 0x10: two extra 16-byte hashes.
5. Two separate uint32 tail fields.

All local entries have zero block hashes and lack the optional 0x10 flag. The payload area starts at the next 1,024-byte boundary after the **physical fetched table position**, not the logical parsed position.

## Payload

Payload physical offset is `data_start + start_block * 1024`. The primary 16-byte hash is MD5 of the final extracted file.

Raw key byte i is `((hash[i%16] + 2 + i%5) * path[i%pathLength] + i) & 255`, then XOR the shared 32-byte key mask. Code units are reduced to their low byte as in the original C# helper.

Compute 32-bit FNV-1a over secret UTF16 code units, starting at `0x811C9DC5`, multiplying by `0x01000193` after each XOR. If this result is h, form raw 12-byte nonce material `LE32(h) || LE32(h >> 1) || LE32((h >> 1) XOR 0x6C)`. XOR nonce byte i with:

`(((17*i) XOR (8*i)) + ((i >> 2) XOR (0x5A + 0x2B*i)) + 11*(i//11) - i) & 255`.

The first 8 bytes become the ChaCha20 nonce; the last 4 bytes become the low 32 bits of its initial 64-bit counter (high bits zero).

For the installed flag values 6 and 7, XOR only the first min(1024, stored size) bytes with consecutive ChaCha20 blocks. Preserve the rest. Flag 1 means zlib decompression; without it, take exactly original size bytes (stored size can include padding). An entry with both sizes zero yields empty data even if flag 1 is set.

## Verification

- All 8,847 installed archives and 318,389 entry tables parsed with valid physical bounds; zero failures.
- The C# implementation also verified every one of the 318,389 payloads against its final-content MD5: 97,101,884,059 bytes, zero failures.
- Flag 7: 307,199 entries. Flag 6: 11,190 entries. Zero block hashes and zero optional 0x10 records throughout.
- First 100 archives: all 2,685 payloads extracted in memory and passed final-content MD5 verification, totaling 1,087,128,820 output bytes. Includes 21 uncompressed entries, 8 package secrets, padding, files over 1,024 bytes and an empty compressed-flag entry.
- Production validation uses `HFSExtract.CLI --verify`, including final-content MD5. Repository tests use independently generated synthetic archives, including the counter reset and physical alignment edge cases.

Useful package examples:

- `000222509AD77BA98DA26342FDF99A54F18E34C7_00000.hfs`: `maps/lobby_beautyshop.bsp`, 9,692,268 bytes, MD5 `11e8bdb33f82ed1c90fa458b5928de83`.
- `012AC359D6275DDCA0E682569F07887483B3DAF5_00000.hfs`: `models/npc/cut/cam/cut_cam.txt`, original 118 / stored 128 bytes, MD5 `4dd8b50dea2c5ad60f83e6d3f4645da3`.
- `014B627D5883BF12553F2E7E3914D8E5078210BA_00000.hfs`: `effects/pet/panda_skill_aggro_ground.efx`, flags 7 with both sizes zero and the empty-content MD5.

## Sources and scope

The investigation started from Naomi's [HFSExtract HFS-v2 branch](https://github.com/neptuwunium/HFSExtract/tree/HFS-v2) and Falkrian's [Opening Vindictus HFS Version 4 Archives](https://reversing.falkrian.com/posts/getting-vindictus-hfs-archives-open-again/) (CC BY 4.0). The formulas and buffer-reset behavior above were then established from the installed Taiwan client and local archive checks. The article's discussion of table buffering alone does not describe the exact-consumption counter reset.

This is a description of the observed format, not a claim that every regional or future HFS variant is supported. No proprietary client binaries, extracted assets, or decompiled client code are distributed in this repository.
