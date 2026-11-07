using System.Text.RegularExpressions;

namespace TriasDeparture.Alexa.Speech;

/// <summary>Converts between spoken line names and TRIAS line names ("Dreier" ↔ "3", "N fünf" ↔ "N5").</summary>
public static partial class LineNames
{
    // "3", "3er", "64e", "64 e", "n5", "n 5"
    [GeneratedRegex(@"^(?<prefix>n)?\s*(?<number>\d{1,3})\s*(?:er)?\s*(?<suffix>[a-z])?$")]
    private static partial Regex DigitPattern();

    private static readonly HashSet<string> FillerWords =
        ["linie", "bus", "bim", "tram", "straßenbahn", "strassenbahn", "nachtbus", "der", "die", "nummer"];

    /// <summary>
    /// Best-effort parse of a raw slot value that Alexa could not resolve, e.g.
    /// "Dreier" → "3", "Linie 64 E" → "64E", "N fünf" → "N5". Returns null if nothing line-like was said.
    /// </summary>
    public static string? Parse(string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken))
            return null;

        var words = spoken.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !FillerWords.Contains(w))
            .ToList();
        if (words.Count == 0)
            return null;

        var digits = DigitPattern().Match(string.Join(' ', words));
        if (digits.Success)
            return $"{digits.Groups["prefix"].Value}{digits.Groups["number"].Value}{digits.Groups["suffix"].Value}".ToUpperInvariant();

        // Number words: optional "n"/"en" prefix, the number, optional letter suffix ("vierundsechzig e")
        var prefix = words[0] is "n" or "en" ? "N" : string.Empty;
        if (prefix.Length > 0) words.RemoveAt(0);
        if (words.Count == 0 || !GermanNumbers.TryParse(words[0], out var number))
            return null;

        var suffix = words.Count > 1 && words[1].Length == 1 ? words[1].ToUpperInvariant() : string.Empty;
        return $"{prefix}{number}{suffix}";
    }

    /// <summary>True for purely numeric lines ("3", "64"), which are spoken as "3er".</summary>
    public static bool IsNumeric(string line) => line.All(char.IsAsciiDigit);
}
