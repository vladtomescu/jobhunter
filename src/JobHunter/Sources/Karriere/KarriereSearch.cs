namespace JobHunter.Sources.Karriere;

/// <summary>One karriere.at search as the settings list it: keywords and, when given, a place, written "keywords | place".</summary>
public sealed record KarriereSearch(string Keywords, string? Place)
{
    private const char PlaceSeparator = '|';

    private const string SearchUrl = "https://www.karriere.at/jobs";

    /// <summary>Reads the searches from the settings text, one per line; a line without keywords is skipped and a repeated search counts once.</summary>
    public static IReadOnlyList<KarriereSearch> ReadAll(string lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return [.. lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Read).OfType<KarriereSearch>().Distinct()];
    }

    /// <summary>The address of one result page of this search, counted from one.</summary>
    public Uri PageUrl(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);

        string place = Place is null ? string.Empty : $"&locations={Uri.EscapeDataString(Place)}";

        return new Uri($"{SearchUrl}?keywords={Uri.EscapeDataString(Keywords)}{place}&page={page}");
    }

    public override string ToString()
    {
        return Place is null ? Keywords : $"{Keywords} | {Place}";
    }

    private static KarriereSearch? Read(string line)
    {
        int separatorIndex = line.IndexOf(PlaceSeparator, StringComparison.Ordinal);
        string keywords = (separatorIndex < 0 ? line : line[..separatorIndex]).Trim();
        string place = separatorIndex < 0 ? string.Empty : line[(separatorIndex + 1)..].Trim();

        return keywords.Length == 0 ? null : new KarriereSearch(keywords, place.Length == 0 ? null : place);
    }
}
