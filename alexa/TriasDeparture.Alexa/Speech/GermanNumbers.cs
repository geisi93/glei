namespace TriasDeparture.Alexa.Speech;

/// <summary>German number words 1–99, e.g. 3 → "drei" / "dreier", 64 → "vierundsechzig" / "vierundsechziger".</summary>
public static class GermanNumbers
{
    private static readonly string[] Small =
    [
        "", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun",
        "zehn", "elf", "zwölf", "dreizehn", "vierzehn", "fünfzehn", "sechzehn", "siebzehn", "achtzehn", "neunzehn"
    ];

    private static readonly string[] Tens =
        ["", "", "zwanzig", "dreißig", "vierzig", "fünfzig", "sechzig", "siebzig", "achtzig", "neunzig"];

    public const int Max = 99;

    /// <summary>Lookup of every cardinal and "-er" word (1–99) back to its number.</summary>
    private static readonly Dictionary<string, int> Parsed = Enumerable.Range(1, Max)
        .SelectMany(n => new[] { (Word: Cardinal(n), Number: n), (Word: Colloquial(n), Number: n) })
        .Append((Word: "ein", Number: 1))
        .Append((Word: "siebner", Number: 7))
        .ToDictionary(x => x.Word, x => x.Number);

    /// <summary>Cardinal number word, e.g. 1 → "eins", 21 → "einundzwanzig".</summary>
    public static string Cardinal(int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, Max);

        if (n < 20) return Small[n];

        var unit = n % 10;
        var tens = Tens[n / 10];
        return unit == 0 ? tens : $"{(unit == 1 ? "ein" : Small[unit])}und{tens}";
    }

    /// <summary>Colloquial line name as used in Graz, e.g. 1 → "einser", 3 → "dreier", 64 → "vierundsechziger".</summary>
    public static string Colloquial(int n) => Cardinal(n) + "er";

    /// <summary>Parses a cardinal or colloquial number word (case-insensitive), e.g. "Dreier" → 3.</summary>
    public static bool TryParse(string word, out int number) =>
        Parsed.TryGetValue(word.Trim().ToLowerInvariant(), out number);
}
