using TriasDeparture.Alexa.Models;
using TriasDeparture.Alexa.Speech;
using TriasDeparture.Application.Interfaces;

namespace TriasDeparture.Alexa.Skill;

/// <summary>A TRIAS stop with the name Alexa speaks.</summary>
public sealed record ResolvedStop(string Id, string Name);

/// <summary>Outcome of resolving the stop slot.</summary>
/// <param name="Stop">The stop, if found.</param>
/// <param name="Spoken">What the user said (null if the slot was empty).</param>
public sealed record StopResolution(ResolvedStop? Stop, string? Spoken)
{
    public bool IsMissing => Stop is null && Spoken is null;
}

/// <summary>Turns slot values into TRIAS stop IDs and line names.</summary>
public sealed class SlotResolver(ITriasClient client)
{
    // The stop list in the interaction model covers Graz; free-text searches prefer it as well
    private const string PreferredLocality = "Graz";

    /// <summary>
    /// Uses Alexa's entity resolution (slot value ID = TRIAS stop ID) and falls back to the
    /// TRIAS stop search for names that aren't in the model.
    /// </summary>
    public async Task<StopResolution> ResolveStopAsync(Slot? slot, CancellationToken ct = default)
    {
        var spoken = slot?.Value?.Trim();
        if (string.IsNullOrEmpty(spoken))
            return new StopResolution(null, null);

        var matches = Matches(slot).Where(v => !string.IsNullOrEmpty(v.Id)).ToList();
        if (matches.Count > 0)
        {
            // Synonyms like "Griesplatz" can match several stops – prefer the one named exactly like that
            var best = matches.FirstOrDefault(v => TextNormalizer.Normalize(v.Name) == TextNormalizer.Normalize(spoken))
                       ?? matches[0];
            return new StopResolution(new ResolvedStop(best.Id!, best.Name), spoken);
        }

        // TRIAS search is fuzzy ("Nirgendwogasse" → "Klagenfurt Morogasse") – only accept names resembling what was said
        var stops = (await client.SearchStopsAsync(spoken, ct))
            .Where(s => TextNormalizer.SimilarTo(WithoutLocality(s.Name), spoken) || TextNormalizer.SimilarTo(s.Name, spoken))
            .ToList();
        var stop  = stops.FirstOrDefault(s => s.Name.StartsWith(PreferredLocality + " ", StringComparison.OrdinalIgnoreCase))
                    ?? stops.FirstOrDefault();

        return stop is null
            ? new StopResolution(null, spoken)
            : new StopResolution(new ResolvedStop(stop.Id, WithoutLocality(stop.Name)), spoken);
    }

    /// <summary>Line name as TRIAS knows it ("3", "64E", "N5"), from the slot value ID or the raw words.</summary>
    public static string? ResolveLine(Slot? slot)
    {
        var id = Matches(slot).Select(v => v.Id).FirstOrDefault(i => !string.IsNullOrEmpty(i));
        return id ?? LineNames.Parse(slot?.Value);
    }

    /// <summary>Canonical value if Alexa resolved it (e.g. a known destination), otherwise what was said.</summary>
    public static string? ResolveText(Slot? slot)
    {
        var spoken = slot?.Value?.Trim();
        if (string.IsNullOrEmpty(spoken))
            return null;

        return Matches(slot).Select(v => v.Name).FirstOrDefault() ?? spoken;
    }

    private static IEnumerable<ResolutionValue> Matches(Slot? slot) =>
        slot?.Resolutions?.ResolutionsPerAuthority?
            .Where(a => a.Status?.Code == "ER_SUCCESS_MATCH")
            .SelectMany(a => a.Values ?? [])
            .Select(v => v.Value)
        ?? [];

    private static string WithoutLocality(string name) =>
        name.StartsWith(PreferredLocality + " ", StringComparison.OrdinalIgnoreCase)
            ? name[(PreferredLocality.Length + 1)..]
            : name;
}
