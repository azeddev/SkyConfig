namespace SkyConfig.Core;

public enum UpgradePath
{
    None,
    Primary,
    Secondary
}

public sealed record UpgradeState(
    UpgradePath Path,
    bool StoredSecondaryPath,
    bool Base1,
    bool Base2,
    bool Base3,
    bool Base4,
    bool Primary1,
    bool Primary2,
    bool Primary3,
    bool Secondary1,
    bool Secondary2,
    bool Secondary3,
    bool SoulGem,
    bool WowPow)
{
    public static UpgradeState Empty { get; } = new(
        UpgradePath.None, false,
        false, false, false, false,
        false, false, false,
        false, false, false,
        false, false);

    public ushort ToRawBits()
    {
        bool secondarySelected = Path switch
        {
            UpgradePath.Primary => false,
            UpgradePath.Secondary => true,
            _ => StoredSecondaryPath
        };

        ushort bits = 0;
        SetBit(ref bits, 0, Path != UpgradePath.None);
        SetBit(ref bits, 1, secondarySelected);
        SetBit(ref bits, 2, Base1);
        SetBit(ref bits, 3, Base2);
        SetBit(ref bits, 4, Base3);
        SetBit(ref bits, 5, Base4);

        bool[] primary = [Primary1, Primary2, Primary3];
        bool[] secondary = [Secondary1, Secondary2, Secondary3];
        bool[] active = secondarySelected ? secondary : primary;
        bool[] alternate = secondarySelected ? primary : secondary;
        for (int index = 0; index < 3; index++)
        {
            SetBit(ref bits, 6 + index, active[index]);
            SetBit(ref bits, 11 + index, alternate[index]);
        }

        SetBit(ref bits, 9, SoulGem);
        SetBit(ref bits, 10, WowPow);
        return bits;
    }

    public static UpgradeState FromRawBits(ushort bits)
    {
        bool secondarySelected = IsSet(bits, 1);
        UpgradePath path = !IsSet(bits, 0)
            ? UpgradePath.None
            : secondarySelected ? UpgradePath.Secondary : UpgradePath.Primary;

        bool[] active = [IsSet(bits, 6), IsSet(bits, 7), IsSet(bits, 8)];
        bool[] alternate = [IsSet(bits, 11), IsSet(bits, 12), IsSet(bits, 13)];
        bool[] primary = secondarySelected ? alternate : active;
        bool[] secondary = secondarySelected ? active : alternate;

        return new UpgradeState(
            path,
            secondarySelected,
            IsSet(bits, 2), IsSet(bits, 3), IsSet(bits, 4), IsSet(bits, 5),
            primary[0], primary[1], primary[2],
            secondary[0], secondary[1], secondary[2],
            IsSet(bits, 9), IsSet(bits, 10));
    }

    private static bool IsSet(ushort bits, int index) => (bits & (1 << index)) != 0;

    private static void SetBit(ref ushort bits, int index, bool value)
    {
        if (value)
            bits |= (ushort)(1 << index);
    }
}

