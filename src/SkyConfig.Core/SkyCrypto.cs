using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SkyConfig.Core;

internal static class SkyCrypto
{
    private const int BlockSize = 16;
    private static readonly byte[] HashConstant =
        Encoding.ASCII.GetBytes(" Copyright (C) 2010 Activision. All Rights Reserved. ");

    public static byte[] Decrypt(ReadOnlySpan<byte> raw)
    {
        byte[] result = raw.ToArray();
        byte[] sectorZero = raw[..0x20].ToArray();

        for (int block = 8; block < 64; block++)
        {
            if (IsSectorTrailer(block))
                continue;

            ReadOnlySpan<byte> encrypted = raw.Slice(block * BlockSize, BlockSize);
            if (IsZero(encrypted))
                continue;

            TransformBlock(encrypted, result.AsSpan(block * BlockSize, BlockSize), sectorZero, block, false);
        }

        return result;
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> plain, ReadOnlySpan<byte> originalRaw)
    {
        byte[] result = plain.ToArray();
        byte[] sectorZero = plain[..0x20].ToArray();

        for (int block = 8; block < 64; block++)
        {
            if (IsSectorTrailer(block))
                continue;

            ReadOnlySpan<byte> source = plain.Slice(block * BlockSize, BlockSize);
            ReadOnlySpan<byte> original = originalRaw.Slice(block * BlockSize, BlockSize);
            if (IsZero(source) && IsZero(original))
            {
                result.AsSpan(block * BlockSize, BlockSize).Clear();
                continue;
            }

            TransformBlock(source, result.AsSpan(block * BlockSize, BlockSize), sectorZero, block, true);
        }

        return result;
    }

    public static void PopulateSectorKeys(Span<byte> data)
    {
        for (byte sector = 0; sector < 16; sector++)
        {
            ulong key = CalculateKeyA(sector, data[..4]);
            int offset = sector * 64 + 48;
            for (int index = 0; index < 6; index++)
                data[offset + 5 - index] = (byte)(key >> (index * 8));
        }
    }

    private static void TransformBlock(
        ReadOnlySpan<byte> input,
        Span<byte> output,
        ReadOnlySpan<byte> sectorZero,
        int block,
        bool encrypt)
    {
        byte[] keyInput = new byte[0x56];
        sectorZero.CopyTo(keyInput);
        keyInput[0x20] = (byte)block;
        HashConstant.CopyTo(keyInput, 0x21);
        byte[] key = MD5.HashData(keyInput);

        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        if (encrypt)
            aes.EncryptEcb(input, output, PaddingMode.None);
        else
            aes.DecryptEcb(input, output, PaddingMode.None);
    }

    private static bool IsSectorTrailer(int block) => (block + 1) % 4 == 0;

    private static bool IsZero(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value != 0)
                return false;
        }

        return true;
    }

    private static ulong CalculateKeyA(byte sector, ReadOnlySpan<byte> uid)
    {
        if (sector == 0)
            return 73UL * 2017UL * 560381651UL;

        Span<byte> input = stackalloc byte[5];
        uid.CopyTo(input);
        input[4] = sector;
        ulong crc = ComputeCrc48(input);
        return BinaryPrimitives.ReverseEndianness(crc) >> 16;
    }

    private static ulong ComputeCrc48(ReadOnlySpan<byte> data)
    {
        const ulong polynomial = 0x42F0E1EBA9EA3693;
        const ulong mask = 0x0000FFFFFFFFFFFF;
        ulong crc = 170325570882756;

        foreach (byte value in data)
        {
            crc ^= (ulong)value << 40;
            for (int bit = 0; bit < 8; bit++)
                crc = ((crc & 0x800000000000) != 0 ? (crc << 1) ^ polynomial : crc << 1) & mask;
        }

        return crc;
    }
}
