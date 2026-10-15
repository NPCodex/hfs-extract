using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using HFSExtract;

namespace HFSExtract.Tests {
    internal static class Program {
        private static int passed;
        private static int failed;
        private static byte[] golden = Array.Empty<byte>();
        private static Manifest fixture = null!;

        private sealed record Entry(string Path, int Flags, int StartBlock, int Size, int Stored,
            int RecordOffset, string ExpectedBase64, string Sha256);
        private sealed record Manifest(string Filename, int HeaderOffset, int TableOffset, int DataOffset,
            int TableLogicalSize, int TablePhysicalSize, int[] TableCounters, string ArchiveSha256, Entry[] Entries);

        public static int Main() {
            var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            golden = File.ReadAllBytes(Path.Combine(directory, "synthetic-v4.bin"));
            fixture = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(directory, "synthetic-v4.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

            TestChaCha20();
            Check("golden fixture identity and boundary coverage", () => {
                Assert(Convert.ToHexString(SHA256.HashData(golden)).Equals(fixture.ArchiveSha256, StringComparison.OrdinalIgnoreCase));
                Assert(fixture.TableOffset + fixture.TableLogicalSize <= 1024);
                Assert(fixture.TableOffset + fixture.TablePhysicalSize > 1024);
                Assert(fixture.TableCounters.SequenceEqual(new[] { 0, 0, 1, 2, 3, 4, 5, 6 }));
            });
            Check("v4 compressed, raw, encrypted, flag-2 and empty payloads", () => {
                using var archive = Open(golden);
                Assert(archive.Version == 4 && archive.Files.Count == fixture.Entries.Length);
                foreach (var entry in fixture.Entries.Reverse()) {
                    var actual = archive.ReadFile(entry.Path);
                    Equal(Convert.FromBase64String(entry.ExpectedBase64), actual.Span);
                    Assert(Convert.ToHexString(SHA256.HashData(actual.Span)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase));
                }
            });
            Check("short underlying reads and uppercase archive filename", () => {
                using var archive = new HFSArchive(new ShortReadStream(golden), fixture.Filename.ToUpperInvariant());
                foreach (var entry in fixture.Entries) {
                    Equal(Convert.FromBase64String(entry.ExpectedBase64), archive.ReadFile(entry.Path).Span);
                }
            });
            Check("missing path reports FileNotFoundException", () => {
                using var archive = Open(golden);
                Reject<FileNotFoundException>(() => archive.ReadFile("not-present.txt"));
            });
            Check("truncated common header rejected", () => RejectArchive(golden[..(fixture.HeaderOffset + 5)]));
            Check("truncated extension rejected", () => RejectArchive(golden[..(fixture.HeaderOffset + 38)]));
            Check("truncated table rejected", () => RejectArchive(golden[..(fixture.TableOffset + 64)]));
            Check("truncated payload range rejected", () => RejectArchive(golden[..^1]));
            Check("corrupt header checksum rejected", () => RejectArchive(Mutate(fixture.HeaderOffset + 31, 1)));
            Check("invalid secret length rejected", () => RejectArchive(Mutate(fixture.HeaderOffset + 1, 1)));
            Check("negative entry count rejected", () => {
                var bytes = Clone();
                ReplaceHeaderCount(bytes, -1);
                Reject<InvalidDataException>(() => { using var archive = Open(bytes); }, "Invalid HFS entry count.");
            });
            Check("impossible entry count rejected", () => {
                var bytes = Clone();
                ReplaceHeaderCount(bytes, int.MaxValue);
                Reject<InvalidDataException>(() => { using var archive = Open(bytes); }, "Invalid HFS entry count.");
            });
            Check("zero-length table path rejected", () => {
                var bytes = Clone();
                ReplaceEncryptedInt32(bytes, fixture.TableOffset, 4, 0);
                RejectArchive(bytes);
            });
            Check("negative block hash count rejected", () => {
                var bytes = Clone();
                ReplaceEncryptedInt32(bytes, fixture.TableOffset + 36, 0, -1);
                RejectArchive(bytes);
            });
            Check("negative start block rejected", () => {
                var bytes = Clone();
                ReplaceEncryptedInt32(bytes, fixture.TableOffset + 20, 0, -1);
                RejectArchive(bytes);
            });
            Check("duplicate table path rejected", () => {
                var entry = fixture.Entries.Single(item => item.Path == "e.tx");
                RejectArchive(Mutate(fixture.TableOffset + entry.RecordOffset + 4, (byte)('e' ^ 'a')));
            });
            Check("corrupt stored payload detected by MD5", () => {
                using var archive = Open(Mutate(fixture.DataOffset, 1));
                Reject<InvalidDataException>(() => archive.ReadFile("a.tx"));
            });
            Check("corrupt table MD5 rejected", () => {
                using var archive = Open(Mutate(fixture.TableOffset + 40, 1));
                Reject<InvalidDataException>(() => archive.ReadFile("a.tx"));
            });
            Check("empty-file MD5 is validated", () => {
                var entry = fixture.Entries.Single(item => item.Path == "d.tx");
                using var archive = Open(Mutate(fixture.TableOffset + entry.RecordOffset + 40, 1));
                Reject<InvalidDataException>(() => archive.ReadFile(entry.Path));
            });
            Check("empty compressed payload is actually decoded", () => {
                var entry = fixture.Entries.Single(item => item.Path == "c.tx");
                using var archive = Open(Mutate(fixture.DataOffset + entry.StartBlock * 1024, 0xff));
                Reject<InvalidDataException>(() => archive.ReadFile(entry.Path));
            });
            Check("decompression shorter than declared size rejected", () => {
                var entry = fixture.Entries[1];
                var bytes = Clone();
                ReplaceEncryptedInt32(bytes, fixture.TableOffset + entry.RecordOffset + 16 + entry.Path.Length * 2,
                    entry.Size, entry.Size + 1);
                using var archive = Open(bytes);
                Reject<EndOfStreamException>(() => archive.ReadFile(entry.Path));
            });
            Check("decompression longer than declared size rejected", () => {
                var entry = fixture.Entries[1];
                var bytes = Clone();
                ReplaceEncryptedInt32(bytes, fixture.TableOffset + entry.RecordOffset + 16 + entry.Path.Length * 2,
                    entry.Size, entry.Size - 1);
                using var archive = Open(bytes);
                Reject<InvalidDataException>(() => archive.ReadFile(entry.Path));
            });
            Check("unsupported payload flags fail explicitly", () => {
                using var archive = Open(Mutate(fixture.TableOffset + 16, 8));
                Reject<NotSupportedException>(() => archive.ReadFile("a.tx"));
            });
            Check("empty payload still rejects unsupported flags", () => {
                var entry = fixture.Entries.Single(item => item.Path == "d.tx");
                using var archive = Open(Mutate(fixture.TableOffset + entry.RecordOffset + 16, 8));
                Reject<NotSupportedException>(() => archive.ReadFile(entry.Path));
            });

            TestV2(directory);

            Console.WriteLine($"Tests: passed={passed}, failed={failed}");
            return failed == 0 ? 0 : 1;
        }

        private static void TestV2(string directory) {
            var legacy = File.ReadAllBytes(Path.Combine(directory, "synthetic-v2.bin"));
            var metadata = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(directory, "synthetic-v2.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
            Check("v2 golden fixture identity and v4 discriminator collision", () => {
                Assert(Convert.ToHexString(SHA256.HashData(legacy)).Equals(metadata.ArchiveSha256, StringComparison.OrdinalIgnoreCase));
                Assert((byte)(legacy[metadata.HeaderOffset] ^ (unchecked((sbyte)legacy[0]) >> 1)) == 4);
            });
            Check("v2 raw, compressed and empty payload compatibility", () => {
                var ordinary = (byte[])legacy.Clone();
                ordinary[0] = 0;
                using var archive = new HFSArchive(new MemoryStream(ordinary, false), metadata.Filename);
                Assert(archive.Version == 2 && archive.Files.Count == metadata.Entries.Length);
                foreach (var entry in metadata.Entries) {
                    Equal(Convert.FromBase64String(entry.ExpectedBase64), archive.ReadFile(entry.Path).Span);
                }
            });
            Check("valid v2 header wins over colliding v4 discriminator", () => {
                using var archive = new HFSArchive(new MemoryStream(legacy, false), metadata.Filename);
                Assert(archive.Version == 2 && archive.Files.Count == metadata.Entries.Length);
                foreach (var entry in metadata.Entries) {
                    Equal(Convert.FromBase64String(entry.ExpectedBase64), archive.ReadFile(entry.Path).Span);
                }
            });
            Check("v2 invalid checksum rejected", () => {
                var damaged = (byte[])legacy.Clone();
                damaged[0] = 0;
                damaged[metadata.HeaderOffset]++;
                Reject<InvalidDataException>(() => {
                    using var archive = new HFSArchive(new MemoryStream(damaged, false), metadata.Filename);
                });
            });
        }

        private static void TestChaCha20() {
            Check("ChaCha20 standard zero key/nonce vector", () => {
                var actual = new byte[64];
                new HFSChaCha20(new byte[32], new byte[8]).Xor(actual);
                Equal(Convert.FromHexString(
                    "76b8e0ada0f13d90405d6ae55386bd28bdd219b8a08ded1aa836efcc8b770dc7" +
                    "da41597c5157488d7724e03fb8d84a376a43b8f41518a11cc387b669b2ee6586"), actual);
            });
            var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
            var nonce = Convert.FromHexString("0000004a00000000");
            Check("ChaCha20 RFC 8439 section 2.3.2 mapped state", () => {
                // Map the RFC's first nonce word to our 64-bit counter's high word.
                var actual = new byte[64];
                new HFSChaCha20(key, nonce, 0x0900000000000001UL).Xor(actual);
                Equal(Convert.FromHexString(
                    "10f1e7e4d13b5915500fdd1fa32071c4c7d1f4c733c068030422aa9ac3d46c4e" +
                    "d2826446079faa0914c2d705d98b02a2b5129cd1de164eb9cbd083e8a2503c4e"), actual);
            });
            Check("ChaCha20 arbitrary segmentation preserves stream position", () => {
                var whole = Enumerable.Range(0, 259).Select(value => (byte)value).ToArray();
                var pieces = (byte[])whole.Clone();
                new HFSChaCha20(key, nonce, 17).Xor(whole);
                var stream = new HFSChaCha20(key, nonce, 17);
                var offset = 0;
                foreach (var count in new[] { 0, 1, 62, 2, 63, 0, 64, 67 }) {
                    stream.Xor(pieces.AsSpan(offset, count));
                    offset += count;
                }
                Equal(whole, pieces);
            });
            Check("ChaCha20 counter carries into high 32 bits", () => {
                var rollover = new byte[128];
                new HFSChaCha20(key, nonce, uint.MaxValue).Xor(rollover);
                var next = new byte[64];
                new HFSChaCha20(key, nonce, 1UL << 32).Xor(next);
                Equal(next, rollover.AsSpan(64));
            });
            Check("ChaCha20 counter exhaustion cannot reuse keystream", () => {
                var stream = new HFSChaCha20(key, nonce, ulong.MaxValue);
                stream.Xor(new byte[63]);
                stream.Xor(new byte[1]);
                Reject<CryptographicException>(() => stream.Xor(new byte[1]));
            });
            Check("ChaCha20 rejects wrong key and nonce sizes", () => {
                Reject<ArgumentException>(() => new HFSChaCha20(new byte[31], nonce));
                Reject<ArgumentException>(() => new HFSChaCha20(key, new byte[12]));
            });
        }

        private static HFSArchive Open(byte[] bytes) => new(new MemoryStream(bytes, false), fixture.Filename);
        private static byte[] Clone() => (byte[])golden.Clone();
        private static byte[] Mutate(int offset, byte xor) {
            var bytes = Clone();
            bytes[offset] ^= xor;
            return bytes;
        }

        private static void ReplaceEncryptedInt32(byte[] bytes, int offset, int before, int after) {
            // ChaCha20 ciphertext XOR the known plaintext difference changes one field
            // without using any production key derivation or cipher to make the fixture.
            Span<byte> difference = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(difference, before ^ after);
            for (var i = 0; i < 4; i++) bytes[offset + i] ^= difference[i];
        }

        private static void ReplaceHeaderCount(byte[] bytes, int count) {
            // Preserve the header checksum while changing its count. Otherwise
            // a checksum failure could hide a missing entry-count guard.
            var offset = fixture.HeaderOffset;
            var checksum = unchecked(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 1, 4))
                + bytes[offset] + 4u + (uint)fixture.Entries.Length);
            for (var i = 0; i < 13; i++) {
                checksum = unchecked(checksum + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 5 + i * 2, 2)));
            }
            var replacement = unchecked(checksum - (uint)fixture.Entries.Length + (uint)count);
            ReplaceEncryptedInt32(bytes, offset + 31, unchecked((int)checksum), unchecked((int)replacement));
            ReplaceEncryptedInt32(bytes, offset + 35, fixture.Entries.Length, count);
        }

        private static void RejectArchive(byte[] bytes) {
            try { using var archive = Open(bytes); }
            catch (InvalidDataException) { return; }
            catch (EndOfStreamException) { return; }
            throw new Exception("Malformed archive was accepted.");
        }

        private static void Reject<T>(Action operation, string? expectedMessage = null) where T : Exception {
            try { operation(); }
            catch (T error) {
                if (expectedMessage != null && error.Message != expectedMessage) {
                    throw new Exception($"Expected error '{expectedMessage}', got '{error.Message}'.");
                }
                return;
            }
            throw new Exception($"Expected {typeof(T).Name}.");
        }

        private static void Assert(bool condition) {
            if (!condition) throw new Exception("Assertion failed.");
        }

        private static void Equal(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
            if (!expected.SequenceEqual(actual)) throw new Exception($"Payload mismatch: expected {expected.Length} bytes, got {actual.Length}.");
        }

        private static void Check(string name, Action test) {
            try {
                test();
                passed++;
                Console.WriteLine($"PASS {name}");
            } catch (Exception error) {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {error.GetType().Name}: {error.Message}");
            }
        }

        private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes, false) {
            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 3));
            public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 3)]);
        }
    }
}
