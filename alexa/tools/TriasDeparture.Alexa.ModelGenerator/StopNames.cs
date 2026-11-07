using System.Text.RegularExpressions;

namespace TriasDeparture.Alexa.ModelGenerator;

/// <summary>Turns TRIAS stop names into Alexa slot values ("Griesplatz/Zweiglgasse/tim" → "Griesplatz Zweiglgasse").</summary>
internal static partial class StopNames
{
    // "/tim" marks tim mobility hubs – not part of how people say the name
    [GeneratedRegex(@"/\s*tim\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimSuffix();

    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex Parenthetical();

    // Alexa slot values: letters, digits, spaces, periods, apostrophes and hyphens
    [GeneratedRegex(@"[^\p{L}\p{N} .'\-]")]
    private static partial Regex Unsupported();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\bSt\.\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SanktAbbreviation();

    /// <summary>Main spoken name: without "/tim" and parentheses, "/" replaced by a space.</summary>
    public static string Value(string name) =>
        Clean(Parenthetical().Replace(TimSuffix().Replace(name, string.Empty), string.Empty));

    /// <summary>Alternative ways to say the stop: each "/" part, the parenthetical, "St." spelled out.</summary>
    public static IEnumerable<string> Synonyms(string name)
    {
        var withoutTim = TimSuffix().Replace(name, string.Empty);
        var parts = Parenthetical().Replace(withoutTim, string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

        var candidates = new List<string>();
        if (parts.Length > 1)
            candidates.AddRange(parts);
        if (Parenthetical().IsMatch(withoutTim))
            candidates.Add(withoutTim.Replace("(", " ").Replace(")", " "));

        candidates.AddRange(candidates.ToList().Append(withoutTim)
            .Where(c => SanktAbbreviation().IsMatch(c))
            .Select(c => SanktAbbreviation().Replace(c, "Sankt ")));

        return candidates.Select(Clean).Where(c => c.Length > 1);
    }

    public static string Clean(string value) =>
        Spaces().Replace(Unsupported().Replace(value.Replace('/', ' ').Replace("&", " und "), " "), " ").Trim();
}
