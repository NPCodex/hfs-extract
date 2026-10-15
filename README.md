# HFSExtract

English | [简体中文](README.zh-CN.md)

Extract Vindictus / Mabinogi Heroes HFS resource archives. This update is based on [neptuwunium/HFSExtract's HFS-v2 branch](https://github.com/neptuwunium/HFSExtract/tree/HFS-v2) and adds **HFS v4** support for the current Taiwan client while retaining the original HFS v2 reader.

## Download and run

Download `HFSExtract-v0.3.0-win-x64.zip` from [Releases](https://github.com/NPCodex/hfs-extract/releases) and extract the ZIP. This Windows x64 build includes the .NET runtime; no separate .NET installation is required. Open PowerShell in the extracted directory and run `HFSExtract.CLI.exe`.

This is a command-line application, not a graphical interface. Keep each archive's original filename: filenames are used to derive encryption keys and offsets.

```powershell
# Show help
.\HFSExtract.CLI.exe --help

# Extract the client's hfs resource directories
.\HFSExtract.CLI.exe "F:\tw_vindictus" "D:\vindictus-extracted"

# Or select an hfs directory directly
.\HFSExtract.CLI.exe "F:\tw_vindictus\appdata\zh-TW\hfs" "D:\vindictus-extracted"

# Extract one archive
.\HFSExtract.CLI.exe "F:\tw_vindictus\appdata\zh-TW\hfs\000222509AD77BA98DA26342FDF99A54F18E34C7_00000.hfs" "D:\vindictus-sample"
```

Input can be a single `.hfs` file, a directory containing archives, or a client root. Client roots are searched for `hfs` subdirectories. A locale directory containing both `hfs` and `Base_*.hfs` uses the `hfs` directory. Archive paths are preserved, for example `D:\vindictus-extracted\materials\...`.

## Options

```powershell
# List metadata without reading payloads or creating output files
.\HFSExtract.CLI.exe "F:\tw_vindictus" --list

# Decrypt, decompress and verify every v4 payload's MD5 without writing files
.\HFSExtract.CLI.exe "F:\tw_vindictus" --verify --quiet

# Extract material definitions (case-insensitive glob; * also matches separators)
.\HFSExtract.CLI.exe "F:\tw_vindictus" "D:\vindictus-materials" --filter "materials/*.vmt"

# Verify matching paths only
.\HFSExtract.CLI.exe "F:\tw_vindictus" --verify --filter "models/*" --quiet

# Explicitly allow replacing existing output files
.\HFSExtract.CLI.exe "F:\tw_vindictus" "D:\vindictus-extracted" --overwrite --quiet
```

| Option | Behavior |
| --- | --- |
| `--list` | Print archive, internal path, original size, stored size and flags; validate metadata only. |
| `--verify` | Decrypt and decompress; verify v4 final-content MD5 against the archive record. Write no output files. |
| `--filter "glob"` | Match the full internal path, case-insensitively; supports `*`, `?`, `/` and `\`. |
| `--quiet` | Suppress per-archive and per-file output; keep errors and the final summary. |
| `--overwrite` | Allow replacing existing output files. By default, existing files cause an error. |
| `--help` / `-h` | Display help. |

`--list` and `--verify` are mutually exclusive. Normal extraction also checks v4 payload MD5 before writing. Files are processed individually in memory, so large files require corresponding free memory. Full extraction also requires sufficient disk space.

The final line is `Summary: archives=..., files=..., bytes=..., failures=...`. `files` and `bytes` count successful entries matching the filter; `failures` includes archive parsing, decryption, verification and output errors. Exit code **0** means the operation succeeded; **1** means invalid arguments or at least one failure. Processing continues after individual failures.

## Compatibility and validation

- Validated against the local Taiwan client snapshot at `F:\tw_vindictus\appdata\zh-TW`, identified by `CurrentInfo.txt` as `2026-09-10 19:54:18.59 / 205-107-227-115`. Validation date: 2026-10-15.
- All 8,847 HFS v4 archives and **318,389 files** passed full decryption, decompression and final-content MD5 verification: **97,101,884,059 bytes, zero failures**.
- Supports the observed `0x06` / `0x07` flags, per-package secrets, empty entries, uncompressed entries and zlib compression.
- HFS v2 retains the original algorithm and is covered by synthetic archive regression tests; a complete historical client installation has not been verified.
- Legacy `Base_hl2.hfs` / `Base_platform.hfs` (HFS v1) are unsupported. HFS v3 support is not claimed. V4 payloads with flag bits outside `0x07` or nonempty block-hash lists produce explicit errors.
- Extraction and verification only read the input archives; game files are not modified.

An invalid header or header checksum usually means a renamed, damaged or unsupported archive. An `MD5 mismatch` means the decoded content does not match its archive record; the entry is reported as a failure and is not written as successful output.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0):

```powershell
dotnet build HFSExtract.sln -c Release
dotnet run --project HFSExtract.Tests -c Release
dotnet run --project HFSExtract.CLI -c Release -- "F:\tw_vindictus" --verify --quiet

# Self-contained Windows x64 executable
dotnet publish HFSExtract.CLI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o artifacts/win-x64
```

Tests use generated archives and contain no game assets. See the [validation report](docs/VALIDATION.md) and [HFS v4 format notes](docs/HFS-v4.md) for details.

## Credits and license

Based on Naomi's HFSExtract (`HFS-v2`, commit `aa0f107`; original project thanks EKey). Distributed under the [MIT License](LICENSE), with the original copyright notice preserved.

The investigation used Falkrian's [Opening Vindictus HFS Version 4 Archives](https://reversing.falkrian.com/posts/getting-vindictus-hfs-archives-open-again/) (article licensed CC BY 4.0) as a reference. This implementation was completed using static analysis of the local client and independent decoding checks. It contains no LaPacker source or game assets.
