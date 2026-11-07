namespace ZibraSharp.Internal.Archivers
{
    /// <summary>
    /// CRC-32 (IEEE 802.3) implementation used by the ZIP format.
    /// </summary>
    internal static class Crc32
    {
        private const uint Polynomial = 0xEDB88320u;
        private static readonly uint[] Table = CreateTable();

        public const uint Initial = 0xFFFFFFFFu;

        public static uint Append(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        public static uint Finish(uint crc) => ~crc;

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0
                        ? (value >> 1) ^ Polynomial
                        : value >> 1;
                }

                table[i] = value;
            }

            return table;
        }
    }
}
