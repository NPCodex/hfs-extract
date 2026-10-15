# HFSExtract

[English](README.md) | 简体中文

解包《洛奇英雄传 / Vindictus》的 HFS 资源包。基于 [neptuwunium/HFSExtract 的 HFS-v2 分支](https://github.com/neptuwunium/HFSExtract/tree/HFS-v2) 更新，支持当前台服客户端使用的 **HFS v4**，保留原有 HFS v2 读取功能。

## 下载与运行

从 [Releases](https://github.com/NPCodex/hfs-extract/releases) 下载 `HFSExtract-v0.3.0-win-x64.zip` 并完整解压。此 Windows x64 构建自带 .NET 运行时，无需另装 .NET。在解压目录打开 PowerShell，运行 `HFSExtract.CLI.exe`。

程序是命令行工具，双击不会打开图形界面。保留资源包原始文件名：文件名参与密钥和偏移计算，重命名会导致解密失败。

```powershell
# 查看帮助
.\HFSExtract.CLI.exe --help

# 解包整个客户端的 hfs 资源目录
.\HFSExtract.CLI.exe "F:\tw_vindictus" "D:\vindictus-extracted"

# 也可以直接指定 hfs 目录
.\HFSExtract.CLI.exe "F:\tw_vindictus\appdata\zh-TW\hfs" "D:\vindictus-extracted"

# 解包单个资源包
.\HFSExtract.CLI.exe "F:\tw_vindictus\appdata\zh-TW\hfs\000222509AD77BA98DA26342FDF99A54F18E34C7_00000.hfs" "D:\vindictus-sample"
```

输入可以是一个 `.hfs` 文件、直接包含资源包的目录，或客户端根目录。客户端根目录会自动寻找 `hfs` 子目录；地区目录同时包含 `hfs` 和 `Base_*.hfs` 时优先使用 `hfs`。输出保留包内路径，例如 `D:\vindictus-extracted\materials\...`。

## 常用选项

```powershell
# 列出文件，不读取资源内容、不创建输出文件
.\HFSExtract.CLI.exe "F:\tw_vindictus" --list

# 全量解密、解压，并核对 v4 每个文件的 MD5；不写入输出文件
.\HFSExtract.CLI.exe "F:\tw_vindictus" --verify --quiet

# 只导出材质文件（glob 不区分大小写，* 可匹配目录分隔符）
.\HFSExtract.CLI.exe "F:\tw_vindictus" "D:\vindictus-materials" --filter "materials/*.vmt"

# 只校验指定路径
.\HFSExtract.CLI.exe "F:\tw_vindictus" --verify --filter "models/*" --quiet

# 显式允许覆盖已有输出文件
.\HFSExtract.CLI.exe "F:\tw_vindictus" "D:\vindictus-extracted" --overwrite --quiet
```

| 参数 | 作用 |
| --- | --- |
| `--list` | 输出包名、包内路径、原始大小、存储大小和标志；仅检查目录结构。 |
| `--verify` | 解密和解压；v4 验证包内记录的最终内容 MD5，不创建输出文件。 |
| `--filter "glob"` | 按包内完整路径筛选；支持 `*` 和 `?`，使用 `/` 或 `\` 均可。 |
| `--quiet` | 隐藏逐包/逐文件输出，保留错误与最后的统计。 |
| `--overwrite` | 允许覆盖已有输出文件；默认拒绝覆盖并报告失败。 |
| `--help` / `-h` | 显示帮助。 |

`--list` 和 `--verify` 不能同时使用。普通解包也会在写入前校验 v4 内容的 MD5。程序以单个文件为单位读取到内存，大文件需要相应的可用内存；全量导出还需要足够的磁盘空间。

最后一行格式为 `Summary: archives=..., files=..., bytes=..., failures=...`。`files` 和 `bytes` 统计成功处理且符合筛选条件的文件，`failures` 包括目录解析、解密、校验和写入错误。退出码 **0** 表示本次操作全部成功，**1** 表示参数错误或至少一项失败。遇到文件失败时继续处理其余文件。

## 兼容性与验证

- 针对 `F:\tw_vindictus\appdata\zh-TW` 的本地客户端快照验证；`CurrentInfo.txt` 标识为 `2026-09-10 19:54:18.59 / 205-107-227-115`。验证日期：2026-10-15。
- 8,847 个 HFS v4 包、**318,389 个文件全部通过解密、解压及最终内容 MD5 校验**；合计 **97,101,884,059 字节，失败数 0**。
- 支持该快照出现的标志 `0x06` / `0x07`、每包独立 secret、空文件、未压缩文件及 zlib 压缩文件。
- HFS v2 保留原有算法，并使用合成资源包进行回归验证；未对真实旧版客户端进行全量验证。
- 不支持旧式 `Base_hl2.hfs` / `Base_platform.hfs`（HFS v1），也不宣称支持 v3。v4 payload 含有 `0x07` 以外的标志位，或 block-hash 列表非空时，会明确报错。
- 解包和校验只读取输入资源包，不修改游戏文件。

`Invalid HFS header` / header checksum 错误通常意味着文件被重命名、资源包损坏或格式不受支持。`MD5 mismatch` 表示输出与包内记录不一致；工具会报错，不将该内容作为成功结果写出。

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

```powershell
dotnet build HFSExtract.sln -c Release
dotnet run --project HFSExtract.Tests -c Release
dotnet run --project HFSExtract.CLI -c Release -- "F:\tw_vindictus" --verify --quiet

# 自带运行时的 Windows x64 单文件程序
dotnet publish HFSExtract.CLI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o artifacts/win-x64
```

测试使用自行生成的资源包，不包含游戏资源。详见[验证报告](docs/VALIDATION.md)和 [HFS v4 格式说明](docs/HFS-v4.md)。

## 致谢与许可证

基于 Naomi 的 HFSExtract（`HFS-v2`，提交 `aa0f107`，原项目致谢 EKey）。遵循 [MIT License](LICENSE)，保留原版权声明。

格式调查参考 Falkrian 的 [Opening Vindictus HFS Version 4 Archives](https://reversing.falkrian.com/posts/getting-vindictus-hfs-archives-open-again/)（文章 CC BY 4.0）。本实现结合本地客户端的静态分析和独立解码验证完成；不包含 LaPacker 源码或游戏数据。
