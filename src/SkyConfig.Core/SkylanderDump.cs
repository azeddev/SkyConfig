using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SkyConfig.Core;

public sealed record CharacterData(
    int Experience,
    int Level,
    ushort Gold,
    uint PlayTime,
    ushort HeroRank,
    string Nickname,
    uint HeroicChallengeFlags,
    UpgradeState Upgrades,
    byte SpyrosAdventureHat,
    byte GiantsHat,
    byte SwapForceOrTrapTeamHat,
    byte SuperChargersHat)
{
    public int CompletedHeroicChallenges => BitOperations.PopCount(HeroicChallengeFlags);
}

public sealed record AreaIntegrity(
    string Name,
    byte Counter,
    byte ExtendedCounter,
    bool HeaderChecksum,
    bool DataChecksum,
    bool CharacterChecksum,
    bool ExtendedChecksum)
{
    public int ValidChecksumCount =>
        (HeaderChecksum ? 1 : 0) +
        (DataChecksum ? 1 : 0) +
        (CharacterChecksum ? 1 : 0) +
        (ExtendedChecksum ? 1 : 0);

    public bool CoreValid => HeaderChecksum && DataChecksum && CharacterChecksum;
}

public sealed record DumpIntegrity(bool HeaderChecksum, AreaIntegrity AreaA, AreaIntegrity AreaB);

public sealed class SkylanderDump
{
    public const int Size = 1024;
    public const int MaxNicknameLength = 15;
    public const int MaxExperience = 197500;
    public const ushort MaxGold = 65000;

    private static readonly int[] LevelThresholds =
    [
        0, 1000, 2200, 3800, 6000, 9000, 13000, 18200, 24800, 33000,
        42700, 53900, 66600, 80800, 96500, 113700, 132400, 152600, 174300, 197500
    ];

    private byte[] _originalRaw;
    private byte[] _data;

    private SkylanderDump(byte[] raw)
    {
        _originalRaw = raw;
        _data = SkyCrypto.Decrypt(raw);
    }

    public ushort CharacterId => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(0x10, 2));
    public ushort VariantId => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(0x1C, 2));
    public uint Serial => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(0, 4));
    public FigureDefinition? Definition => FigureCatalog.Find(CharacterId, VariantId);
    public DumpIntegrity Integrity => InspectIntegrity();

    public int ActiveAreaIndex
    {
        get
        {
            DumpIntegrity integrity = Integrity;
            if (integrity.AreaA.ValidChecksumCount != integrity.AreaB.ValidChecksumCount)
                return integrity.AreaA.ValidChecksumCount > integrity.AreaB.ValidChecksumCount ? 0 : 1;

            byte a = integrity.AreaA.Counter;
            byte b = integrity.AreaB.Counter;
            if (a == b)
                return 0;

            return unchecked((byte)(a - b)) < 128 ? 0 : 1;
        }
    }

    public bool SupportsCharacterData =>
        Definition?.SupportsCharacterData == true ||
        Integrity.AreaA.CoreValid || Integrity.AreaB.CoreValid;

    public static SkylanderDump Load(ReadOnlySpan<byte> data)
    {
        if (data.Length != Size)
            throw new InvalidDataException($"A Skylander dump must be exactly {Size} bytes; this file is {data.Length} bytes.");

        return new SkylanderDump(data.ToArray());
    }

    public static SkylanderDump Create(ushort id, ushort variant)
    {
        byte[] data = new byte[Size];
        RandomNumberGenerator.Fill(data.AsSpan(0, 4));
        data[4] = (byte)(data[0] ^ data[1] ^ data[2] ^ data[3]);
        data[5] = 0x81;
        data[6] = 0x01;
        data[7] = 0x0F;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x10, 2), id);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1C, 2), variant);
        WriteHeaderChecksum(data);

        SkyCrypto.PopulateSectorKeys(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x36, 4), 0x690F0F0F);
        for (int sector = 1; sector < 16; sector++)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sector * 64 + 0x36, 4), 0x69080F7F);

        return new SkylanderDump(data);
    }

    public CharacterData ReadCharacterData()
    {
        if (!SupportsCharacterData)
            throw new InvalidOperationException("This figure type does not use the standard character data layout.");

        int area = AreaOffset(ActiveAreaIndex);
        int experience = ReadExperience(area);
        return new CharacterData(
            experience,
            LevelFromExperience(experience),
            ReadUInt16(area + 0x03),
            ReadUInt32(area + 0x05),
            ReadUInt16(area + 0x5A),
            ReadNickname(area),
            ReadUInt32(area + 0x56),
            ReadUpgrades(area),
            _data[area + 0x14],
            _data[area + 0x95],
            _data[area + 0x9C],
            _data[area + 0x9E]);
    }

    public void ApplyIdentity(ushort id, ushort variant)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(0x10, 2), id);
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(0x1C, 2), variant);
        WriteHeaderChecksum(_data);
    }

    public void ApplyCharacterData(CharacterData value)
    {
        if (!SupportsCharacterData)
            throw new InvalidOperationException("This figure type does not use the standard character data layout.");
        if (value.Experience is < 0 or > MaxExperience)
            throw new ArgumentOutOfRangeException(nameof(value), $"Experience must be between 0 and {MaxExperience}.");
        if (value.Gold > MaxGold)
            throw new ArgumentOutOfRangeException(nameof(value), $"Gold must be between 0 and {MaxGold}.");
        if (value.Nickname.Length > MaxNicknameLength)
            throw new ArgumentOutOfRangeException(nameof(value), $"Nicknames are limited to {MaxNicknameLength} characters.");

        int activeIndex = ActiveAreaIndex;
        int targetIndex = 1 - activeIndex;
        CopyArea(activeIndex, targetIndex);

        int active = AreaOffset(activeIndex);
        int target = AreaOffset(targetIndex);
        WriteExperience(target, value.Experience);
        WriteUInt16(target + 0x03, value.Gold);
        WriteUInt32(target + 0x05, value.PlayTime);
        WriteUInt16(target + 0x5A, value.HeroRank);
        WriteNickname(target, value.Nickname);
        WriteUInt32(target + 0x56, value.HeroicChallengeFlags);

        WriteUpgrades(target, value.Upgrades);
        _data[target + 0x14] = value.SpyrosAdventureHat;
        _data[target + 0x95] = value.GiantsHat;
        _data[target + 0x9C] = value.SwapForceOrTrapTeamHat;
        _data[target + 0x9E] = value.SuperChargersHat;

        _data[target + 0x09] = unchecked((byte)(_data[active + 0x09] + 1));
        _data[target + 0x92] = unchecked((byte)(_data[active + 0x92] + 1));
        WriteAreaChecksums(_data, target);
    }

    public byte[] ToEncryptedBytes()
    {
        WriteHeaderChecksum(_data);
        byte[] encrypted = SkyCrypto.Encrypt(_data, _originalRaw);
        SkylanderDump validation = Load(encrypted);
        if (!validation.Integrity.HeaderChecksum)
            throw new InvalidDataException("The generated dump failed its header checksum validation.");
        if (SupportsCharacterData && !validation.Integrity.AreaA.CoreValid && !validation.Integrity.AreaB.CoreValid)
            throw new InvalidDataException("The generated dump failed its character data checksum validation.");
        return encrypted;
    }

    public string GetHexDump(bool decrypted)
    {
        byte[] currentEncrypted = decrypted ? [] : SkyCrypto.Encrypt(_data, _originalRaw);
        ReadOnlySpan<byte> bytes = decrypted ? _data : currentEncrypted;
        var text = new StringBuilder(64 * 70);
        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            text.Append(offset.ToString("X3")).Append("  ");
            for (int index = 0; index < 16; index++)
                text.Append(bytes[offset + index].ToString("X2")).Append(index == 7 ? "  " : " ");
            text.Append(' ');
            for (int index = 0; index < 16; index++)
            {
                byte value = bytes[offset + index];
                text.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }
            text.AppendLine();
        }
        return text.ToString();
    }

    public static int ExperienceForLevel(int level)
    {
        if (level is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(level));
        return LevelThresholds[level - 1];
    }

    public static int LevelFromExperience(int experience)
    {
        for (int index = LevelThresholds.Length - 1; index >= 0; index--)
        {
            if (experience >= LevelThresholds[index])
                return index + 1;
        }
        return 1;
    }

    private DumpIntegrity InspectIntegrity()
    {
        ushort stored = ReadUInt16(0x1E);
        ushort computed = Crc16Ccitt.Compute(_data.AsSpan(0, 0x1E));
        return new DumpIntegrity(stored == computed, InspectArea(0), InspectArea(1));
    }

    private AreaIntegrity InspectArea(int index)
    {
        int offset = AreaOffset(index);
        byte[] type1 = _data.AsSpan(offset, 0x10).ToArray();
        type1[0x0E] = 0x05;
        type1[0x0F] = 0x00;

        byte[] type2 = new byte[0x30];
        _data.AsSpan(offset + 0x10, 0x20).CopyTo(type2);
        _data.AsSpan(offset + 0x40, 0x10).CopyTo(type2.AsSpan(0x20));

        byte[] type3 = new byte[0x110];
        _data.AsSpan(offset + 0x50, 0x20).CopyTo(type3);
        _data.AsSpan(offset + 0x80, 0x10).CopyTo(type3.AsSpan(0x20));

        byte[] type6 = BuildExtendedChecksumInput(_data, offset);

        return new AreaIntegrity(
            index == 0 ? "A" : "B",
            _data[offset + 0x09],
            _data[offset + 0x92],
            ReadUInt16(offset + 0x0E) == Crc16Ccitt.Compute(type1),
            ReadUInt16(offset + 0x0C) == Crc16Ccitt.Compute(type2),
            ReadUInt16(offset + 0x0A) == Crc16Ccitt.Compute(type3),
            ReadUInt16(offset + 0x90) == Crc16Ccitt.Compute(type6));
    }

    private static void WriteAreaChecksums(Span<byte> data, int offset)
    {
        byte[] type3 = new byte[0x110];
        data.Slice(offset + 0x50, 0x20).CopyTo(type3);
        data.Slice(offset + 0x80, 0x10).CopyTo(type3.AsSpan(0x20));
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset + 0x0A, 2), Crc16Ccitt.Compute(type3));

        byte[] type2 = new byte[0x30];
        data.Slice(offset + 0x10, 0x20).CopyTo(type2);
        data.Slice(offset + 0x40, 0x10).CopyTo(type2.AsSpan(0x20));
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset + 0x0C, 2), Crc16Ccitt.Compute(type2));

        byte[] type6 = BuildExtendedChecksumInput(data, offset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset + 0x90, 2), Crc16Ccitt.Compute(type6));

        byte[] type1 = data.Slice(offset, 0x10).ToArray();
        type1[0x0E] = 0x05;
        type1[0x0F] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset + 0x0E, 2), Crc16Ccitt.Compute(type1));
    }

    private static byte[] BuildExtendedChecksumInput(ReadOnlySpan<byte> data, int offset)
    {
        byte[] input = new byte[0x40];
        data.Slice(offset + 0x90, 0x20).CopyTo(input);
        data.Slice(offset + 0xC0, 0x20).CopyTo(input.AsSpan(0x20));
        input[0] = 0x06;
        input[1] = 0x01;
        return input;
    }

    private void CopyArea(int sourceIndex, int targetIndex)
    {
        int sourceBlock = sourceIndex == 0 ? 8 : 36;
        int targetBlock = targetIndex == 0 ? 8 : 36;
        for (int relativeBlock = 0; relativeBlock < 27; relativeBlock++)
        {
            if ((sourceBlock + relativeBlock + 1) % 4 == 0)
                continue;
            _data.AsSpan((sourceBlock + relativeBlock) * 16, 16)
                .CopyTo(_data.AsSpan((targetBlock + relativeBlock) * 16, 16));
        }
    }

    private int ReadExperience(int area) =>
        ReadUInt16(area) + ReadUInt16(area + 0x93) + ReadUInt24(area + 0x98);

    private void WriteExperience(int area, int experience)
    {
        int first = Math.Min(experience, 33000);
        int remaining = experience - first;
        int second = Math.Min(remaining, 63500);
        int third = remaining - second;
        WriteUInt16(area, (ushort)first);
        WriteUInt16(area + 0x93, (ushort)second);
        WriteUInt24(area + 0x98, third);
    }

    private UpgradeState ReadUpgrades(int area)
    {
        int flags1 = ReadUInt24(area + 0x10);
        ushort flags2 = ReadUInt16(area + 0x96);
        ushort upgradeBits = (ushort)((flags1 & 0x03FF) | ((flags2 & 0x000F) << 10));
        return UpgradeState.FromRawBits(upgradeBits);
    }

    private void WriteUpgrades(int area, UpgradeState upgrades)
    {
        ushort upgradeBits = upgrades.ToRawBits();
        int flags1 = ReadUInt24(area + 0x10);
        flags1 = (flags1 & ~0x03FF) | (upgradeBits & 0x03FF);
        WriteUInt24(area + 0x10, flags1);

        ushort flags2 = ReadUInt16(area + 0x96);
        flags2 = (ushort)((flags2 & ~0x000F) | ((upgradeBits >> 10) & 0x000F));
        WriteUInt16(area + 0x96, flags2);
    }

    private string ReadNickname(int area)
    {
        Span<char> chars = stackalloc char[MaxNicknameLength];
        int length = 0;
        for (int index = 0; index < MaxNicknameLength; index++)
        {
            int position = index < 8 ? area + 0x20 + index * 2 : area + 0x40 + (index - 8) * 2;
            char value = (char)ReadUInt16(position);
            if (value == '\0')
                break;
            chars[length++] = value;
        }
        return new string(chars[..length]);
    }

    private void WriteNickname(int area, string nickname)
    {
        _data.AsSpan(area + 0x20, 0x10).Clear();
        _data.AsSpan(area + 0x40, 0x0E).Clear();
        for (int index = 0; index < nickname.Length; index++)
        {
            int position = index < 8 ? area + 0x20 + index * 2 : area + 0x40 + (index - 8) * 2;
            WriteUInt16(position, nickname[index]);
        }
    }

    private static void WriteHeaderChecksum(Span<byte> data) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(0x1E, 2), Crc16Ccitt.Compute(data[..0x1E]));

    private static int AreaOffset(int index) => index == 0 ? 0x80 : 0x240;
    private ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset, 2));
    private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4));
    private int ReadUInt24(int offset) => _data[offset] | _data[offset + 1] << 8 | _data[offset + 2] << 16;
    private void WriteUInt16(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(offset, 2), (ushort)value);
    private void WriteUInt32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(offset, 4), value);
    private void WriteUInt24(int offset, int value)
    {
        _data[offset] = (byte)value;
        _data[offset + 1] = (byte)(value >> 8);
        _data[offset + 2] = (byte)(value >> 16);
    }
}
