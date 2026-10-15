using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace HFSExtract {
    // Original ChaCha20 layout: 256-bit key, 64-bit block counter, 64-bit nonce.
    // This is a stream cipher, not the IETF 96-bit nonce or ChaCha20-Poly1305 construction.
    internal sealed class HFSChaCha20 {
        private readonly uint[] state = new uint[16];
        private readonly byte[] block = new byte[64];
        private int blockOffset = 64;
        private bool exhausted;

        public HFSChaCha20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ulong counter = 0) {
            if (key.Length != 32) {
                throw new ArgumentException("ChaCha20 requires a 32-byte key.", nameof(key));
            }
            if (nonce.Length != 8) {
                throw new ArgumentException("Original ChaCha20 requires an 8-byte nonce.", nameof(nonce));
            }

            state[0] = 0x61707865;
            state[1] = 0x3320646e;
            state[2] = 0x79622d32;
            state[3] = 0x6b206574;
            for (var i = 0; i < 8; i++) {
                state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(i * 4, 4));
            }
            state[12] = (uint)counter;
            state[13] = (uint)(counter >> 32);
            state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[..4]);
            state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[4..]);
        }

        public void Xor(Span<byte> data) {
            while (!data.IsEmpty) {
                if (blockOffset == block.Length) {
                    GenerateBlock();
                }
                var count = Math.Min(data.Length, block.Length - blockOffset);
                for (var i = 0; i < count; i++) {
                    data[i] ^= block[blockOffset + i];
                }
                blockOffset += count;
                data = data[count..];
            }
        }

        private void GenerateBlock() {
            if (exhausted) {
                throw new CryptographicException("ChaCha20 block counter exhausted.");
            }
            Span<uint> working = stackalloc uint[16];
            state.AsSpan().CopyTo(working);
            for (var i = 0; i < 10; i++) {
                QuarterRound(ref working[0], ref working[4], ref working[8], ref working[12]);
                QuarterRound(ref working[1], ref working[5], ref working[9], ref working[13]);
                QuarterRound(ref working[2], ref working[6], ref working[10], ref working[14]);
                QuarterRound(ref working[3], ref working[7], ref working[11], ref working[15]);
                QuarterRound(ref working[0], ref working[5], ref working[10], ref working[15]);
                QuarterRound(ref working[1], ref working[6], ref working[11], ref working[12]);
                QuarterRound(ref working[2], ref working[7], ref working[8], ref working[13]);
                QuarterRound(ref working[3], ref working[4], ref working[9], ref working[14]);
            }
            for (var i = 0; i < working.Length; i++) {
                BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(i * 4, 4), unchecked(working[i] + state[i]));
            }
            blockOffset = 0;
            if (state[12] == uint.MaxValue && state[13] == uint.MaxValue) {
                exhausted = true;
            } else {
                state[12] = unchecked(state[12] + 1);
                if (state[12] == 0) {
                    state[13] = unchecked(state[13] + 1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void QuarterRound(ref uint a, ref uint b, ref uint c, ref uint d) {
            unchecked {
                a += b;
                d = BitOperations.RotateLeft(d ^ a, 16);
                c += d;
                b = BitOperations.RotateLeft(b ^ c, 12);
                a += b;
                d = BitOperations.RotateLeft(d ^ a, 8);
                c += d;
                b = BitOperations.RotateLeft(b ^ c, 7);
            }
        }
    }
}
