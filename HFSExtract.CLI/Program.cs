using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

namespace HFSExtract.CLI {
    internal static class Program {
        private sealed class Options {
            public string Input { get; set; } = "";
            public string? Output { get; set; }
            public string? Filter { get; set; }
            public bool List { get; set; }
            public bool Verify { get; set; }
            public bool Quiet { get; set; }
            public bool Overwrite { get; set; }
        }

        public static int Main(string[] args) {
            if (args.Any(arg => arg is "--help" or "-h")) {
                PrintHelp();
                return 0;
            }

            Options options;
            string[] archives;
            try {
                options = ParseOptions(args);
                archives = FindArchives(options.Input);
                if (archives.Length == 0) {
                    throw new ArgumentException("No .hfs archives found in the input directory or its hfs subdirectories.");
                }
            } catch (Exception error) {
                Console.Error.WriteLine($"Error: {error.Message}");
                Console.Error.WriteLine("Use --help for usage.");
                return 1;
            }

            long archiveCount = 0;
            long fileCount = 0;
            long byteCount = 0;
            long failureCount = 0;
            if (options.List && !options.Quiet) {
                Console.WriteLine("archive\tpath\tbytes\tstored_bytes\tflags");
            }

            foreach (var archivePath in archives) {
                archiveCount++;
                var archiveName = Path.GetFileName(archivePath);
                if (!options.Quiet && !options.List) {
                    Console.WriteLine($"Archive: {archivePath}");
                }
                try {
                    using var stream = File.OpenRead(archivePath);
                    using var archive = new HFSArchive(stream, archiveName);
                    foreach (var entry in archive.Files) {
                        var filename = entry.Key;
                        if (options.Filter != null && !FileSystemName.MatchesSimpleExpression(
                                options.Filter, filename.Replace('\\', '/'), ignoreCase: true)) {
                            continue;
                        }

                        try {
                            long size;
                            if (options.List) {
                                size = entry.Value.File.FileSize;
                                if (!options.Quiet) {
                                    Console.WriteLine($"{archiveName}\t{filename}\t{size}\t{entry.Value.File.BufferSize}\t{entry.Value.File.Flags}");
                                }
                            } else {
                                // Validate the payload before creating any output.
                                var data = archive.ReadFile(filename);
                                size = data.Length;
                                if (!options.Verify) {
                                    var target = GetSafeOutputPath(options.Output!, filename);
                                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                    WriteOutputFile(target, data.Span, options.Overwrite);
                                }
                                if (!options.Quiet) {
                                    Console.WriteLine($"{(options.Verify ? "Verified" : "Extracted")}: {filename} ({size} bytes)");
                                }
                            }
                            fileCount++;
                            byteCount += size;
                        } catch (Exception error) {
                            failureCount++;
                            Console.Error.WriteLine($"Error [{archiveName} :: {filename}]: {error.Message}");
                        }
                    }
                } catch (Exception error) {
                    failureCount++;
                    Console.Error.WriteLine($"Error [{archivePath}]: {error.Message}");
                }
            }

            Console.WriteLine($"Summary: archives={archiveCount}, files={fileCount}, bytes={byteCount}, failures={failureCount}");
            return failureCount == 0 ? 0 : 1;
        }

        private static void WriteOutputFile(string target, ReadOnlySpan<byte> data, bool overwrite) {
            var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".hfs-extract-{Guid.NewGuid():N}.tmp");
            try {
                // Close a complete temporary file before replacing or creating the final path.
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    output.Write(data);
                }
                File.Move(temporary, target, overwrite);
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static Options ParseOptions(string[] args) {
            var options = new Options();
            var positional = new List<string>();
            var endOfOptions = false;
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (endOfOptions) {
                    positional.Add(arg);
                    continue;
                }
                switch (arg) {
                    case "--": endOfOptions = true; break;
                    case "--list": options.List = true; break;
                    case "--verify": options.Verify = true; break;
                    case "--quiet": options.Quiet = true; break;
                    case "--overwrite": options.Overwrite = true; break;
                    case "--filter":
                        if (++i >= args.Length || string.IsNullOrEmpty(args[i])) {
                            throw new ArgumentException("--filter requires a path glob, for example \"materials/*\".");
                        }
                        options.Filter = args[i].Replace('\\', '/');
                        break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal)) {
                            throw new ArgumentException($"Unknown option: {arg}");
                        }
                        positional.Add(arg);
                        break;
                }
            }

            if (options.List && options.Verify) {
                throw new ArgumentException("Choose either --list or --verify.");
            }
            if (positional.Count < 1 || positional.Count > 2) {
                throw new ArgumentException("Expected an input path and, for extraction, an output directory.");
            }
            if (!options.List && !options.Verify && positional.Count != 2) {
                throw new ArgumentException("Extraction requires an output directory.");
            }
            options.Input = Path.GetFullPath(positional[0]);
            // List and verify accept a legacy output argument but never access or create it.
            if (positional.Count == 2 && !options.List && !options.Verify) {
                options.Output = Path.GetFullPath(positional[1]);
            }
            return options;
        }

        private static string[] FindArchives(string input) {
            if (File.Exists(input)) {
                if (!input.EndsWith(".hfs", StringComparison.OrdinalIgnoreCase)) {
                    throw new ArgumentException("The input file must have the .hfs extension.");
                }
                return new[] { input };
            }
            if (!Directory.Exists(input)) {
                throw new DirectoryNotFoundException($"Input path does not exist: {input}");
            }

            var hfsDirectory = Path.Combine(input, "hfs");
            IEnumerable<string> archivePaths;
            if (Directory.Exists(hfsDirectory)) {
                // Locale roots also contain legacy Base_*.hfs files; prefer their current hfs directory.
                archivePaths = EnumerateArchives(hfsDirectory, recursive: true);
            } else {
                var directArchives = EnumerateArchives(input, recursive: false).ToArray();
                if (directArchives.Length != 0) {
                    archivePaths = directArchives;
                } else {
                    archivePaths = Directory.EnumerateDirectories(input, "hfs", RecursiveOptions())
                        .SelectMany(directory => EnumerateArchives(directory, recursive: true));
                }
            }
            return archivePaths.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static IEnumerable<string> EnumerateArchives(string directory, bool recursive) {
            var enumeration = RecursiveOptions();
            enumeration.RecurseSubdirectories = recursive;
            return Directory.EnumerateFiles(directory, "*", enumeration)
                .Where(path => path.EndsWith(".hfs", StringComparison.OrdinalIgnoreCase));
        }

        private static EnumerationOptions RecursiveOptions() => new() {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        private static string GetSafeOutputPath(string output, string archivePath) {
            var normalized = archivePath.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized)) {
                throw new InvalidDataException("Archive entry has an empty or absolute output path.");
            }
            var segments = normalized.Split('/');
            foreach (var segment in segments) {
                if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':') ||
                    segment.EndsWith('.') || segment.EndsWith(' ')) {
                    throw new InvalidDataException("Archive entry contains an unsafe output path component.");
                }
                if (IsReservedDeviceName(segment.Split('.')[0])) {
                    throw new InvalidDataException("Archive entry uses a reserved device name.");
                }
            }

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
            var target = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException("Archive entry escapes the output directory.");
            }

            // Existing links could redirect a valid relative path outside the output directory.
            var current = root;
            RejectLink(current);
            foreach (var segment in segments) {
                current = Path.Combine(current, segment);
                RejectLink(current);
            }
            return target;
        }

        private static void RejectLink(string path) {
            try {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) {
                    throw new IOException($"Refusing to write through a symbolic link or reparse point: {path}");
                }
            } catch (FileNotFoundException) {
                // A new path will be created during extraction.
            } catch (DirectoryNotFoundException) {
                // A new path will be created during extraction.
            }
        }

        private static bool IsReservedDeviceName(string name) {
            if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("NUL", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return name.Length == 4 && name[3] is >= '1' and <= '9' &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
        }

        private static void PrintHelp() {
            Console.WriteLine("HFSExtract - Vindictus HFS extractor");
            Console.WriteLine("Usage: HFSExtract.CLI <input> <output> [options]");
            Console.WriteLine("       HFSExtract.CLI <input> --list|--verify [options]");
            Console.WriteLine();
            Console.WriteLine("Input: one .hfs file, an archive directory, or a client root containing hfs subdirectories.");
            Console.WriteLine("  --list          List archive metadata without reading file payloads.");
            Console.WriteLine("  --verify        Decrypt, decompress and verify v4 MD5 without writing files.");
            Console.WriteLine("  --filter <glob> Select archive paths (case-insensitive; * and ? wildcards).");
            Console.WriteLine("  --quiet         Show only the final summary and errors.");
            Console.WriteLine("  --overwrite     Allow replacing existing output files; default is to report a failure.");
            Console.WriteLine("  --help, -h      Show this help.");
            Console.WriteLine();
            Console.WriteLine("Example: HFSExtract.CLI C:\\Games\\Vindictus D:\\Extracted --filter \"materials/*\"");
            Console.WriteLine("List and verify ignore an optional output argument and never create output files.");
            Console.WriteLine("The summary counts archive attempts, successful selected files, their bytes, and failures.");
            Console.WriteLine("Exit status is 0 on success and 1 if an archive or file fails.");
        }
    }
}
