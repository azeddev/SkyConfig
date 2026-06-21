using SkyConfig.Core;

var tests = new (string Name, Action Run)[]
{
    ("Catalog loads", CatalogLoads),
    ("Upgrade encoding covers all flag combinations", UpgradeEncoding),
    ("Generated dumps round-trip exactly", GeneratedDumpsRoundTrip),
    ("Nickname-only changes persist", NicknameOnlyMutation),
    ("Character fields can be changed safely", CharacterMutation),
    ("Identity changes re-key encrypted blocks", IdentityMutation),
    ("New dumps are valid", NewDump),
    ("Invalid file sizes are rejected", InvalidSize)
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void CatalogLoads()
{
    Assert(FigureCatalog.Figures.Count >= 500, "The embedded figure catalog is incomplete.");
    Assert(FigureCatalog.Find(16, 0)?.Name == "Spyro", "Spyro was not found.");
    Assert(FigureCatalog.Find(2009, 0)?.Type == "Swapper", "Trap Shadow (Top) has the wrong type.");
    Assert(FigureCatalog.Find(1009, 0)?.Type == "Swapper", "Trap Shadow (Bottom) has the wrong type.");
    Assert(FigureCatalog.Find(601, 0)?.SupportsCharacterData == true, "Imaginators Senseis were not classified as characters.");
    Assert(UpgradeCatalog.NamedProfileCount >= 140, "The embedded upgrade catalog is incomplete.");
    UpgradeProfile spyro = UpgradeCatalog.Find(16, FigureCatalog.Find(16, 0));
    Assert(spyro.HasNamedData && spyro.BaseUpgrades[0] == "Long Range Raze", "Spyro's upgrades were not found.");
}

static void UpgradeEncoding()
{
    for (ushort bits = 0; bits < 0x4000; bits++)
    {
        UpgradeState state = UpgradeState.FromRawBits(bits);
        Assert(state.ToRawBits() == bits, $"Upgrade flags 0x{bits:X4} did not round-trip.");
    }
}

static void GeneratedDumpsRoundTrip()
{
    (ushort Id, ushort Variant)[] figures =
    [
        (16, 0),
        (4, 0),
        (2009, 0),
        (1009, 0),
        (212, 0x300E)
    ];

    foreach ((ushort id, ushort variant) in figures)
    {
        SkylanderDump created = SkylanderDump.Create(id, variant);
        if (created.SupportsCharacterData)
            created.ApplyCharacterData(created.ReadCharacterData());

        byte[] source = created.ToEncryptedBytes();
        SkylanderDump dump = SkylanderDump.Load(source);
        Assert(dump.Integrity.HeaderChecksum, $"Figure {id}, variant 0x{variant:X4} has an invalid header checksum.");
        byte[] saved = dump.ToEncryptedBytes();
        Assert(source.SequenceEqual(saved), $"Figure {id}, variant 0x{variant:X4} changed during a no-op round trip.");
    }
}

static void CharacterMutation()
{
    SkylanderDump dump = CreateValidCharacterDump();
    CharacterData original = dump.ReadCharacterData();
    var changed = original with
    {
        Experience = 197500,
        Level = 20,
        Gold = 65000,
        PlayTime = 123456,
        HeroRank = 17,
        Nickname = "Test Skylander!",
        HeroicChallengeFlags = 0x0000000F,
        Upgrades = original.Upgrades with
        {
            Path = UpgradePath.Secondary,
            Base1 = true,
            Base2 = true,
            Base3 = true,
            Base4 = true,
            Primary1 = true,
            Primary2 = false,
            Primary3 = true,
            Secondary1 = true,
            Secondary2 = true,
            Secondary3 = false,
            SoulGem = true,
            WowPow = true
        },
        SpyrosAdventureHat = 4,
        GiantsHat = 50,
        SwapForceOrTrapTeamHat = 100,
        SuperChargersHat = 200
    };

    int oldArea = dump.ActiveAreaIndex;
    dump.ApplyCharacterData(changed);
    SkylanderDump reloaded = SkylanderDump.Load(dump.ToEncryptedBytes());
    CharacterData actual = reloaded.ReadCharacterData();

    Assert(reloaded.ActiveAreaIndex != oldArea, "The inactive save area was not advanced.");
    Assert(actual.Experience == changed.Experience, "Experience did not persist.");
    Assert(actual.Level == 20, "Level did not persist.");
    Assert(actual.Gold == changed.Gold, "Gold did not persist.");
    Assert(actual.PlayTime == changed.PlayTime, "Play time did not persist.");
    Assert(actual.HeroRank == changed.HeroRank, "Hero rank did not persist.");
    Assert(actual.Nickname == changed.Nickname, "Nickname did not persist.");
    Assert(actual.HeroicChallengeFlags == changed.HeroicChallengeFlags, "Heroic flags did not persist.");
    Assert(actual.CompletedHeroicChallenges == 4, "Heroic flag count is wrong.");
    Assert(actual.Upgrades.ToRawBits() == changed.Upgrades.ToRawBits(), "Upgrade selections did not persist.");
    Assert(actual.SuperChargersHat == 200, "Hat values did not persist.");
    Assert(reloaded.Integrity.AreaA.CoreValid && reloaded.Integrity.AreaB.CoreValid,
        "A character save area failed validation after editing.");
}

static void NicknameOnlyMutation()
{
    SkylanderDump dump = CreateValidCharacterDump();
    CharacterData before = dump.ReadCharacterData();
    string nickname = before.Nickname == "Nickname Only" ? "Changed Name" : "Nickname Only";

    dump.ApplyCharacterData(before with { Nickname = nickname });
    SkylanderDump reloaded = SkylanderDump.Load(dump.ToEncryptedBytes());

    Assert(reloaded.ReadCharacterData().Nickname == nickname, "A nickname-only edit did not persist.");
    Assert(reloaded.Integrity.AreaA.CoreValid && reloaded.Integrity.AreaB.CoreValid,
        "A save area failed validation after a nickname-only edit.");
}

static void IdentityMutation()
{
    SkylanderDump dump = CreateValidCharacterDump();
    CharacterData before = dump.ReadCharacterData();
    dump.ApplyIdentity(4, 0);
    dump.ApplyCharacterData(before);

    SkylanderDump reloaded = SkylanderDump.Load(dump.ToEncryptedBytes());
    Assert(reloaded.CharacterId == 4, "The character ID did not persist.");
    Assert(reloaded.Definition?.Name == "Bash", "The changed identity was not recognized.");
    Assert(reloaded.ReadCharacterData().Experience == before.Experience, "Progress was lost while re-keying.");
}

static void NewDump()
{
    SkylanderDump created = SkylanderDump.Create(16, 0);
    CharacterData data = created.ReadCharacterData() with
    {
        Experience = SkylanderDump.ExperienceForLevel(10),
        Level = 10,
        Gold = 999,
        Nickname = "New Spyro"
    };
    created.ApplyCharacterData(data);

    byte[] bytes = created.ToEncryptedBytes();
    Assert(bytes.Length == SkylanderDump.Size, "The new dump has the wrong size.");
    SkylanderDump reloaded = SkylanderDump.Load(bytes);
    Assert(reloaded.Integrity.HeaderChecksum, "The new dump header is invalid.");
    Assert(reloaded.Integrity.AreaB.CoreValid, "The new dump character area is invalid.");
    Assert(reloaded.ReadCharacterData().Nickname == "New Spyro", "New dump data did not persist.");
}

static void InvalidSize()
{
    bool threw = false;
    try
    {
        SkylanderDump.Load(new byte[100]);
    }
    catch (InvalidDataException)
    {
        threw = true;
    }
    Assert(threw, "A short file was accepted.");
}

static SkylanderDump CreateValidCharacterDump()
{
    SkylanderDump created = SkylanderDump.Create(16, 0);
    created.ApplyCharacterData(created.ReadCharacterData());
    return SkylanderDump.Load(created.ToEncryptedBytes());
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
