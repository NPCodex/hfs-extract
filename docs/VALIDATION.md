# Validation — 2026-10-15

## Local client

- Locale: Taiwan (`zh-TW`).
- `CurrentInfo.txt`: `Information Date=2026-09-10 Time=19:54:18.59 Code=205-107-227-115`.
- Reader DLL SHA-256: `2e58616e9856934431111904958fa9573ecaf09af4baee620a1d50c662f34ab7`.
- Source baseline: `neptuwunium/HFSExtract`, `HFS-v2`, `aa0f107e7fbfb6b4b63763c3afb955baddd18271`.

## Full C# validation

```text
HFSExtract.CLI F:\tw_vindictus --verify --quiet
Summary: archives=8847, files=318389, bytes=97101884059, failures=0
Exit code: 0
```

Every payload was decrypted, decompressed when needed, checked against its declared size, and compared with the final-content MD5 stored in its archive. This includes all five non-ASCII paths observed in the installation. Input archives were opened for reading only; the full validation did not write extracted game files.

Flags: 307,199 entries with `0x07`; 11,190 with `0x06`. All entries had zero block hashes. No `0x10` optional-hash payloads occurred. Normalized Windows output paths were unique across all 318,389 entries.

## Independent and output checks

A separate Python decoder independently parsed all 8,847 archives. It also decrypted and verified MD5 for all 2,685 entries in the first 100 archives (1,087,128,820 output bytes), covering eight package secrets, empty entries, padding, uncompressed entries and data spanning the 1,024-byte cipher limit.

CLI checks covered metadata-only listing, verification without output files, glob filtering, single archives and client-root discovery, successful extraction, default overwrite refusal, explicit overwrite and a locked destination. Output is written to a temporary file in the destination directory and moved into place only after a successful write. The failed-move check preserved the existing file and left no temporary file behind.

All 34 regression checks passed, and the Release solution build completed with zero warnings and zero errors using .NET SDK 8.0.426. Repository regression fixtures contain generated data, not game assets. They cover ChaCha20 vectors, stream boundaries, malformed metadata, content corruption, empty entries and HFS v2 compatibility. See [the test documentation](../HFSExtract.Tests/README.md) and run:

```powershell
dotnet run --project HFSExtract.Tests -c Release
```

## Limits

These results establish compatibility with this installed client snapshot. They do not establish support for unobserved flags, encrypted block hashes, other regions or future format revisions. HFS v2 is tested with synthetic archives; HFS v1 and v3 are outside this release's supported scope.

## 中文摘要

2026-10-15 对本地台服快照完成全量验证：8,847 个包、318,389 个文件、97,101,884,059 字节，全部通过解密、解压、大小检查和包内 MD5 校验，失败数为 0。验证过程只读输入文件，不导出整套游戏资源。另有独立 Python 解码验证、合成资源包回归测试和实际输出/覆盖失败测试。以上结论仅针对所列客户端快照。
