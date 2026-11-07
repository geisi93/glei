using System.Text;

namespace TriasDeparture.Alexa.Speech;

/// <summary>Normalizes names for tolerant comparison ("St. Leonhard/Klinikum" ≈ "sankt leonhard klinikum").</summary>
public static class TextNormalizer
{
    /// <summary>Lower-case, umlauts/ß transliterated, punctuation removed, "st" expanded to "sankt", single spaces.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                default:  sb.Append(char.IsLetterOrDigit(c) ? c : ' '); break;
            }
        }

        var words = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w == "st" ? "sankt" : w);
        return string.Join(' ', words);
    }

    /// <summary>
    /// True if one normalized text contains the other as a whole-word sequence,
    /// e.g. "liebenau" ↔ "liebenau murpark" or "hauptbahnhof" ↔ "graz hauptbahnhof vorplatz".
    /// </summary>
    public static bool SimilarTo(string? a, string? b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        if (x.Length == 0 || y.Length == 0)
            return false;

        return ContainsWords(x, y) || ContainsWords(y, x);
    }

    private static bool ContainsWords(string haystack, string needle) =>
        $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);
}
