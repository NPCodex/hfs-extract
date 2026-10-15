using System;
using System.IO;

namespace HFSExtract {
    // Physical bytes fetched, including cached bytes, determine the payload base.
    internal sealed class HFSV4TableReader : Stream {
        private readonly Stream source;
        private readonly byte[] key;
        private byte[] block = Array.Empty<byte>();
        private int cursor;
        private ulong counter;

        public HFSV4TableReader(Stream source, byte[] key) {
            this.source = source;
            this.key = key;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) {
            var length = buffer.Length;
            while (!buffer.IsEmpty) {
                if (cursor == block.Length) {
                    block = new byte[checked((buffer.Length + 63) / 64 * 64)];
                    source.ReadExactly(block);
                    new HFSChaCha20(key, new byte[8], counter).Xor(block);
                    counter += (ulong)block.Length / 64;
                    cursor = 0;
                }
                var available = block.Length - cursor;
                // The native stream resets its counter only when a caller's read
                // ends EXACTLY at the cache boundary. Preserve individual field reads.
                if (buffer.Length == available) counter = 0;
                var count = Math.Min(buffer.Length, available);
                block.AsSpan(cursor, count).CopyTo(buffer);
                cursor += count;
                buffer = buffer[count..];
            }
            return length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
