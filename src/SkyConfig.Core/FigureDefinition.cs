using System.Globalization;
using System.Reflection;

namespace SkyConfig.Core;

public sealed record FigureDefinition(
    ushort Id,
    ushort Variant,
    string Name,
    string Game,
    string Element,
    string Type)
{
    public string DisplayName => $"{Name}  [{Id}, 0x{Variant:X4}]";

    public bool SupportsCharacterData => Type is "Skylander" or "Giant" or "Swapper" or "TrapMaster" or "Mini";
}

public static class FigureCatalog
{
    private static readonly Lazy<IReadOnlyList<FigureDefinition>> LazyFigures = new(Load);

    public static IReadOnlyList<FigureDefinition> Figures => LazyFigures.Value;

    public static FigureDefinition? Find(ushort id, ushort variant) =>
        Figures.FirstOrDefault(item => item.Id == id && item.Variant == variant);

    public static IEnumerable<FigureDefinition> Search(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Figures;

        return Figures.Where(item =>
            item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            item.Id.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<FigureDefinition> Load()
    {
        string resourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Single(name => name.EndsWith("skylanders.tsv", StringComparison.Ordinal));
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var result = new List<FigureDefinition>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] columns = line.Split('\t');
            if (columns.Length != 6)
                throw new InvalidDataException($"Invalid figure catalog row: {line}");

            result.Add(new FigureDefinition(
                ushort.Parse(columns[0], CultureInfo.InvariantCulture),
                Convert.ToUInt16(columns[1], 16),
                columns[2], columns[3], columns[4], columns[5]));
        }

        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Variant)
            .ToArray();
    }
}

