using System.Globalization;
using System.Reflection;
using System.Text;

namespace SkyConfig.Core;

public sealed record UpgradeProfile(
    string FigureName,
    IReadOnlyList<string> BaseUpgrades,
    string PrimaryPathName,
    IReadOnlyList<string> PrimaryUpgrades,
    string SecondaryPathName,
    IReadOnlyList<string> SecondaryUpgrades,
    string SoulGemName,
    string WowPowName,
    string SourceUrl,
    bool HasNamedData)
{
    public static UpgradeProfile Generic(string figureName) => new(
        figureName,
        ["Base upgrade 1", "Base upgrade 2", "Base upgrade 3", "Base upgrade 4"],
        "Primary path",
        ["Primary upgrade 1", "Primary upgrade 2", "Primary upgrade 3"],
        "Secondary path",
        ["Secondary upgrade 1", "Secondary upgrade 2", "Secondary upgrade 3"],
        "Soul Gem",
        "Wow Pow / Sky-Chi",
        string.Empty,
        false);
}

public static class UpgradeCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, UpgradeProfile>> LazyProfiles = new(Load);

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["bushwack"] = "bushwhack",
        ["cobracandabra"] = "cobracadabra"
    };

    public static int NamedProfileCount => LazyProfiles.Value.Count;

    public static UpgradeProfile Find(ushort figureId, FigureDefinition? definition = null)
    {
        string displayName = definition?.Name ?? $"Figure {figureId}";
        IEnumerable<string> candidates = FigureCatalog.Figures
            .Where(item => item.Id == figureId)
            .OrderBy(item => item.Variant == 0 ? 0 : 1)
            .Select(item => item.Name)
            .Prepend(displayName);

        foreach (string name in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string key = Normalize(RemoveVariantSuffix(name));
            if (Aliases.TryGetValue(key, out string? alias))
                key = alias;
            if (LazyProfiles.Value.TryGetValue(key, out UpgradeProfile? profile))
                return profile with { FigureName = displayName };
        }

        return UpgradeProfile.Generic(displayName);
    }

    private static string RemoveVariantSuffix(string name)
    {
        int parenthesis = name.IndexOf(" (", StringComparison.Ordinal);
        return parenthesis < 0 ? name : name[..parenthesis];
    }

    private static string Normalize(string value)
    {
        string formD = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (char character in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, UpgradeProfile> Load()
    {
        string resourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Single(name => name.EndsWith("upgrades.tsv", StringComparison.Ordinal));
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, UpgradeProfile>(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] columns = line.Split('\t');
            if (columns.Length != 17)
                throw new InvalidDataException($"Invalid upgrade catalog row: {line}");

            result[columns[0]] = new UpgradeProfile(
                columns[1], columns[2..6], columns[6], columns[7..10],
                columns[10], columns[11..14], columns[14], columns[15], columns[16], true);
        }

        return result;
    }
}
