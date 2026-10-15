using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HFSExtract {
    public sealed class HFSArchive : IDisposable {
        private const int BlockSize = 1024;
        private readonly Stream baseStream;
        private readonly Dictionary<string, V4Entry> v4Entries = new(StringComparer.Ordinal);
        private long dataOffset;
        private string secret = "";
        public byte Version { get; private set; }
        public Dictionary<string, (HFSFile File, byte[] Hash)> Files { get; } = new(StringComparer.Ordinal);

        private sealed record V4Entry(uint Reserved, byte[][] BlockHashes, uint TailA, uint TailB);

        public HFSArchive(Stream stream, string fileName) {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentException.ThrowIfNullOrEmpty(fileName);
            if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("HFS requires a readable, seekable stream.", nameof(stream));
            baseStream = stream;
            fileName = Path.GetFileName(fileName).ToLowerInvariant();
            var headerOffset = HFSUtils.CalculateHeaderOffset(fileName);
            RequireRange(headerOffset, 12);
            stream.Position = 0;
            var prefix = new byte[headerOffset];
            stream.ReadExactly(prefix);
            var encodedVersion = stream.ReadByte();
            var decodedVersion = (byte)(encodedVersion ^ (unchecked((sbyte)prefix[0]) >> 1));
            // A legacy encrypted byte can coincidentally decode to 4. Give a
            // validated legacy header precedence over the one-byte v4 marker.
            if (HasValidV2Header(fileName, headerOffset)) ReadV2(fileName, headerOffset);
            else if (decodedVersion == 4) ReadV4(fileName, prefix, headerOffset);
            else throw new InvalidDataException("Invalid or unsupported HFS header. Keep the original archive filename; legacy Base_*.hfs files are not supported.");
            foreach (var pair in Files) {
                var file = pair.Value.File;
                if (file.StartBlock < 0 || file.FileSize < 0 || file.BufferSize < 0)
                    throw new InvalidDataException($"Negative size or block offset: {pair.Key}");
                RequireRange(dataOffset + (long)file.StartBlock * BlockSize, file.BufferSize);
                if (!file.Flags.HasFlag(HFSFileFlags.Compressed) && file.FileSize > file.BufferSize)
                    throw new InvalidDataException($"Stored data is shorter than the file: {pair.Key}");
            }
        }

        private bool HasValidV2Header(string name, int headerOffset) {
            if (name.Length != 50) return false;
            var position = baseStream.Position;
            baseStream.Position = headerOffset;
            Span<byte> bytes = stackalloc byte[12];
            baseStream.ReadExactly(bytes);
            baseStream.Position = position;
            var cipher = new HFSSerpent();
            cipher.SetKey(HFSUtils.GenerateKey(name));
            cipher.Decrypt(bytes);
            var header = MemoryMarshal.Read<HFSHeader>(bytes);
            return header.Version == 2 && header.IsValid && header.Count >= 0 && header.Count <= baseStream.Length / 40;
        }

        private void ReadV4(string name, byte[] prefix, int headerOffset) {
            Version = 4;
            using var reader = new BinaryReader(baseStream, Encoding.Unicode, true);
            var encodedLength = reader.ReadUInt32();
            var length = (int)encodedLength ^ (unchecked((sbyte)prefix[0]) >> 1);
            if (length != 13) throw new InvalidDataException("Invalid HFS v4 package secret length. Keep the original archive filename.");
            uint expectedChecksum = unchecked(encodedLength + (byte)(Version ^ (unchecked((sbyte)prefix[0]) >> 1)) + 4u);
            var chars = new char[length];
            for (var i = 0; i < length; i++) {
                var encoded = reader.ReadUInt16();
                expectedChecksum = unchecked(expectedChecksum + encoded);
                chars[i] = (char)(encoded ^ (unchecked((sbyte)prefix[i]) >> 1) ^ 0x4b);
            }
            secret = new string(chars);
            var extension = new byte[8];
            baseStream.ReadExactly(extension);
            new HFSChaCha20(HFSV4Keys.Package(name, secret, false), new byte[8]).Xor(extension);
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(extension);
            var count = BinaryPrimitives.ReadInt32LittleEndian(extension.AsSpan(4));
            ValidateCount(count);
            if (checksum != unchecked(expectedChecksum + (uint)count))
                throw new InvalidDataException("HFS v4 header checksum mismatch. The archive may be damaged or renamed.");
            // Common header + extension + v4 filename-dependent padding (64 instead of 33).
            var tableOffset = headerOffset + 39 + HFSUtils.CalculateEntryTableOffset(name) + 31;
            RequireRange(tableOffset, count == 0 ? 0 : 64);
            baseStream.Position = tableOffset;
            using var table = new HFSV4TableReader(baseStream, HFSV4Keys.Package(name, secret, true));
            using var entries = new BinaryReader(table, Encoding.Unicode, true);
            for (var i = 0; i < count; i++) {
                var path = ReadPath(entries);
                var file = ReadRecord(entries);
                var reserved = entries.ReadUInt32();
                var hashCount = entries.ReadInt32();
                if (hashCount < 0 || hashCount > baseStream.Length / 16)
                    throw new InvalidDataException($"Invalid block hash count: {path}");
                var hashes = new byte[hashCount][];
                for (var j = 0; j < hashCount; j++) hashes[j] = ReadExactly(entries, 16);
                var hash = ReadExactly(entries, 16);
                if (((uint)file.Flags & 0x10) != 0) {
                    ReadExactly(entries, 16);
                    ReadExactly(entries, 16);
                }
                var tailA = entries.ReadUInt32();
                var tailB = entries.ReadUInt32();
                if (!Files.TryAdd(path, (file, hash))) throw new InvalidDataException($"Duplicate archive path: {path}");
                v4Entries.Add(path, new V4Entry(reserved, hashes, tailA, tailB));
            }
            dataOffset = Align(baseStream.Position, BlockSize);
        }

        private void ReadV2(string name, int headerOffset) {
            if (name.Length != 50) throw new InvalidDataException("Unsupported HFS header. This tool reads encrypted HFS v2/v4 archives, not legacy Base_*.hfs files. Keep the original archive filename.");
            baseStream.Position = headerOffset;
            var headerBytes = new byte[12];
            baseStream.ReadExactly(headerBytes);
            var cipher = new HFSSerpent();
            cipher.SetKey(HFSUtils.GenerateKey(name));
            cipher.Decrypt(headerBytes);
            var header = MemoryMarshal.Read<HFSHeader>(headerBytes);
            if (!header.IsValid || header.Version != 2)
                throw new InvalidDataException("Invalid or unsupported HFS header. Keep the original archive filename.");
            Version = header.Version;
            ValidateCount(header.Count);
            var tableOffset = headerOffset + 9 + HFSUtils.CalculateEntryTableOffset(name);
            RequireRange(tableOffset, 0);
            baseStream.Position = tableOffset;
            var size = (int)Math.Min(baseStream.Length - tableOffset, (long)296 * header.Count);
            var bytes = new byte[size];
            baseStream.ReadExactly(bytes);
            cipher.SetKey(HFSUtils.GenerateEncodingKey(name));
            cipher.Decrypt(bytes);
            using var table = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(table, Encoding.Unicode, true);
            for (var i = 0; i < header.Count; i++) {
                var path = ReadPath(reader);
                var file = ReadRecord(reader);
                var hash = ReadExactly(reader, 16);
                if (!Files.TryAdd(path, (file, hash))) throw new InvalidDataException($"Duplicate archive path: {path}");
            }
            dataOffset = Align(tableOffset + table.Position, BlockSize);
        }

        private void ValidateCount(int count) {
            if (count < 0 || count > baseStream.Length / 40)
                throw new InvalidDataException("Invalid HFS entry count.");
        }

        private static string ReadPath(BinaryReader reader) {
            var length = reader.ReadInt32();
            if (length <= 0 || length > 32767) throw new InvalidDataException("Invalid HFS path length.");
            var path = new UnicodeEncoding(false, false, true).GetString(ReadExactly(reader, checked(length * 2)));
            if (path.IndexOf('\0') >= 0) throw new InvalidDataException("HFS path contains a null character.");
            return path;
        }

        private static HFSFile ReadRecord(BinaryReader reader) => new(
            reader.ReadUInt32(), (HFSFileFlags)reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        private static byte[] ReadExactly(BinaryReader reader, int count) {
            var bytes = new byte[count];
            reader.BaseStream.ReadExactly(bytes);
            return bytes;
        }

        private static long Align(long value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

        private void RequireRange(long offset, long size) {
            if (offset < 0 || size < 0 || offset > baseStream.Length || size > baseStream.Length - offset)
                throw new InvalidDataException($"HFS data range exceeds archive length (offset={offset}, size={size}).");
        }

        public ReadOnlyMemory<byte> ReadFile(string path) {
            if (!Files.TryGetValue(path, out var pair)) throw new FileNotFoundException("Path does not exist in the HFS archive.", path);
            var file = pair.File;
            if (Version == 4 && (((uint)file.Flags & ~7u) != 0 || v4Entries[path].BlockHashes.Length != 0))
                throw new NotSupportedException($"Unsupported HFS v4 payload flags or block hashes: {path} (0x{(uint)file.Flags:x}).");
            if (file.FileSize == 0 && file.BufferSize == 0) {
                VerifyHash(path, ReadOnlySpan<byte>.Empty, pair.Hash);
                return ReadOnlyMemory<byte>.Empty;
            }
            baseStream.Position = dataOffset + (long)file.StartBlock * BlockSize;
            var buffer = new byte[file.BufferSize];
            baseStream.ReadExactly(buffer);
            if (Version == 4) {
                DecodeV4(path, file, pair.Hash, buffer);
            } else {
                if (file.Flags.HasFlag(HFSFileFlags.Encrypted)) {
                    var cipher = new HFSSerpent();
                    cipher.SetKey(HFSUtils.GenerateHashedKey(path, pair.Hash));
                    cipher.Decrypt(buffer);
                }
                if (file.Flags.HasFlag(HFSFileFlags.BlockEncrypted)) {
                    var cipher = new HFSSerpent();
                    cipher.SetKey(HFSUtils.GenerateHashedKey(path, pair.Hash));
                    cipher.Decrypt(buffer.AsSpan(0, Math.Min(buffer.Length, BlockSize)));
                }
            }
            if (!file.Flags.HasFlag(HFSFileFlags.Compressed)) {
                var result = buffer.AsMemory(0, file.FileSize);
                VerifyHash(path, result.Span, pair.Hash);
                return result;
            }
            using var compressed = new MemoryStream(buffer, false);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            var data = new byte[file.FileSize];
            zlib.ReadExactly(data);
            if (zlib.ReadByte() != -1) throw new InvalidDataException($"Decompressed data exceeds the declared size: {path}");
            VerifyHash(path, data, pair.Hash);
            return data;
        }

        private void VerifyHash(string path, ReadOnlySpan<byte> data, byte[] expected) {
            if (Version == 4 && !MD5.HashData(data).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException($"HFS payload MD5 mismatch: {path}");
        }

        private void DecodeV4(string path, HFSFile file, byte[] hash, byte[] buffer) {
            if (file.Flags.HasFlag(HFSFileFlags.BlockEncrypted)) {
                var nonce = HFSV4Keys.Nonce(secret);
                var counter = BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(8));
                new HFSChaCha20(HFSV4Keys.Payload(path, hash), nonce.AsSpan(0, 8), counter)
                    .Xor(buffer.AsSpan(0, Math.Min(buffer.Length, BlockSize)));
            }
            // V4's flag 0x02 wrapper has a transform limit of zero (passes through).
        }

        public void Dispose() => baseStream.Dispose();
    }
}
