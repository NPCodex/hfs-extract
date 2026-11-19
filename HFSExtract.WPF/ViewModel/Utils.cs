using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HFSExtract.WPF.ViewModel
{
    internal class Utils
    {
        private static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Windows 保留的设备名称（不区分大小写）
        private static readonly string[] ReservedDeviceNames =
        {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };


        public static bool IsValidWindowsPath(string path, bool requireRooted = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // 1. 检查是否要求必须是绝对路径（如 C:\... 或 \\server\share）
            if (requireRooted && !Path.IsPathRooted(path))
                return false;

            try
            {
                // 2. 利用 Path.GetFullPath 校验基础格式与路径长度限制
                string fullPath = Path.GetFullPath(path);

                // 3. 分离根目录与子路径，单独检查非法字符
                string root = Path.GetPathRoot(path) ?? string.Empty;
                string relativePart = path.Substring(root.Length);

                // Windows 文件/目录名中禁止出现的字符（注意冒号只允许出现在盘符中）
                char[] invalidWinChars = { '<', '>', ':', '"', '|', '?', '*' };
                if (relativePart.IndexOfAny(invalidWinChars) >= 0)
                    return false;

                // 4. 逐级检查路径中的每一段（目录名和文件名）
                string[] segments = relativePart.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (string segment in segments)
                {
                    // 不能包含控制字符 (ASCII 0-31)
                    if (segment.Any(c => c < 32))
                        return false;

                    // Windows 文件夹和文件名不能以空格或句点结尾（"." 和 ".." 相对路径符号除外）
                    if (segment != "." && segment != ".." && (segment.EndsWith(" ") || segment.EndsWith(".")))
                        return false;

                    // 不能使用 Windows 保留设备名（即使带扩展名如 CON.txt 也不合法）
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
                    if (ReservedDeviceNames.Contains(nameWithoutExt))
                        return false;
                }

                return true;
            }
            catch (Exception)
            {
                // Path.GetFullPath 在遇到空字符、格式错误或超长路径时会抛出异常
                return false;
            }

        }

        public static T? LoadJson<T>(string path)
        {
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, options);
        }

        public static string SaveJson<T>(string path, T data)
        {
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(path, json);
            return path;
        }

        public static void OpenByNotepad(string path)
        {
            if (!File.Exists(path)) return;
            var startInfo = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
        }

        public static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }

            return $"{number:n2} {suffixes[counter]}";
        }


        public static void WriteOutputFile(string target, ReadOnlySpan<byte> data, bool overwrite)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".hfs-extract-{Guid.NewGuid():N}.tmp");
            try
            {
                // Close a complete temporary file before replacing or creating the final path.
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(data);
                }
                File.Move(temporary, target, overwrite);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static string GetSafeOutputPath(string output, string archivePath)
        {
            var normalized = archivePath.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            {
                throw new InvalidDataException("Archive entry has an empty or absolute output path.");
            }
            var segments = normalized.Split('/');
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':') ||
                    segment.EndsWith('.') || segment.EndsWith(' '))
                {
                    throw new InvalidDataException("Archive entry contains an unsafe output path component.");
                }
                if (IsReservedDeviceName(segment.Split('.')[0]))
                {
                    throw new InvalidDataException("Archive entry uses a reserved device name.");
                }
            }

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
            var target = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Archive entry escapes the output directory.");
            }

            // Existing links could redirect a valid relative path outside the output directory.
            var current = root;
            RejectLink(current);
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                RejectLink(current);
            }
            return target;
        }

        public static void RejectLink(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Refusing to write through a symbolic link or reparse point: {path}");
                }
            }
            catch (FileNotFoundException)
            {
                // A new path will be created during extraction.
            }
            catch (DirectoryNotFoundException)
            {
                // A new path will be created during extraction.
            }
        }

        public static bool IsReservedDeviceName(string name)
        {
            if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return name.Length == 4 && name[3] is >= '1' and <= '9' &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
        }

    }
}
