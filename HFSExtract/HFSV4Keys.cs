using System;
using System.Buffers.Binary;

namespace HFSExtract {
    internal static class HFSV4Keys {
        private static readonly byte[] Mask = Convert.FromHexString(
            "7B2F3548439502B9AE91A6E1D8D624B433101D3DC1BBC6F4A5FEB3696B56E475");

        public static byte[] Package(string name, string secret, bool table) {
            var text = name.ToLowerInvariant() + secret;
            var key = new byte[32];
            for (var i = 0; i < key.Length; i++) {
                var value = table ? (i % 3 + 2) * text[^(i % text.Length + 1)] : text[i % text.Length];
                key[i] = (byte)((byte)(value + i) ^ Mask[i]);
            }
            return key;
        }

        public static byte[] Payload(string path, byte[] hash) {
            var key = new byte[32];
            for (var i = 0; i < key.Length; i++) {
                key[i] = (byte)((byte)((hash[i % 16] + 2 + i % 5) * path[i % path.Length] + i) ^ Mask[i]);
            }
            return key;
        }

        public static byte[] Nonce(string secret) {
            uint hash = 2166136261;
            foreach (var ch in secret) hash = unchecked((hash ^ ch) * 16777619);
            var bytes = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, hash);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), hash >> 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (hash >> 1) ^ 0x6c);
            for (var i = 0; i < bytes.Length; i++) {
                var mask = ((17 * i) ^ (8 * i)) + ((i >> 2) ^ (0x5a + 0x2b * i)) + 11 * (i / 11) - i;
                bytes[i] ^= (byte)mask;
            }
            return bytes;
        }
    }
}
