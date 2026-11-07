using TriasDeparture.Domain.Models;

namespace TriasDeparture.Alexa.Speech;

/// <summary>Departures of one line in one direction, labelled with the destination of the next trip.</summary>
public sealed record DirectionGroup(string Destination, IReadOnlyList<DepartureDto> Trips);

/// <summary>Builds the German sentences Alexa speaks.</summary>
public static class AnswerFormatter
{
    // ── Grouping ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits departures into directions (TRIAS DirectionRef, falling back to the destination),
    /// skipping cancelled trips; ordered by the next departure in each direction.
    /// </summary>
    public static IReadOnlyList<DirectionGroup> GroupByDirection(IEnumerable<DepartureDto> departures) =>
        departures
            .Where(d => !d.Cancelled)
            .OrderBy(d => d.EffectiveTime)
            .GroupBy(d => d.Direction ?? d.Destination)
            .Select(g => new DirectionGroup(g.First().Destination, g.ToList()))
            .ToList();

    // ── Answers for a specific line ──────────────────────────────────────────

    /// <summary>
    /// One direction: "Der nächste 4er Richtung Liebenau fährt in 3 Minuten, der übernächste in 18 Minuten."
    /// Several:       "Der 4er Richtung Liebenau fährt in 3 und in 18 Minuten, Richtung Reininghaus in 7 und in 22 Minuten."
    /// </summary>
    public static string LineAnswer(string line, string stopName, IReadOnlyList<DirectionGroup> groups)
    {
        var noun = LineNoun.For(line);

        if (groups.Count == 0)
            return NoTrips(noun, stopName);

        if (groups.Count == 1)
        {
            var group = groups[0];
            var first = $"{Capitalize(noun.Article)} nächste {noun.Name} Richtung {group.Destination} fährt {Minutes(group.Trips[0].Minutes)}";
            return group.Trips.Count > 1
                ? $"{first}, {noun.Article} übernächste {Minutes(group.Trips[1].Minutes)}."
                : $"{first}. Eine weitere Abfahrt habe ich nicht gefunden.";
        }

        // "fährt" only after the first direction: "Der 4er Richtung A fährt in …, Richtung B in …"
        var parts = groups.Select((g, i) =>
            $"Richtung {g.Destination} {(i == 0 ? "fährt " : string.Empty)}{MinutesPair(g.Trips)}");
        return $"{Capitalize(noun.Article)} {noun.Name} {string.Join(", ", parts)}.";
    }

    /// <summary>"An der Haltestelle Krenngasse habe ich keinen 4er mehr gefunden."</summary>
    public static string NoTrips(LineNoun noun, string stopName) =>
        $"An der Haltestelle {stopName} habe ich {noun.None} {noun.Name} mehr gefunden.";

    /// <summary>Prefix when the requested direction matches no trip: "Richtung Webling habe ich keinen 4er gefunden."</summary>
    public static string DirectionNotFound(string direction, string? line) =>
        line is null
            ? $"Richtung {direction} habe ich keine Abfahrt gefunden."
            : $"Richtung {direction} habe ich {LineNoun.For(line).None} {LineNoun.For(line).Name} gefunden.";

    // ── Answer without a line ────────────────────────────────────────────────

    /// <summary>"An der Haltestelle Krenngasse fährt als nächstes der 3er Richtung Andritz in 2 Minuten, danach der 5er Richtung Puntigam in 4 Minuten."</summary>
    public static string AnyLineAnswer(string stopName, IEnumerable<DepartureDto> departures)
    {
        var next = departures.Where(d => !d.Cancelled).OrderBy(d => d.EffectiveTime).Take(2).ToList();
        if (next.Count == 0)
            return $"An der Haltestelle {stopName} habe ich keine Abfahrten mehr gefunden.";

        var first = $"An der Haltestelle {stopName} fährt als nächstes {Trip(next[0])}";
        return next.Count > 1 ? $"{first}, danach {Trip(next[1])}." : $"{first}.";
    }

    private static string Trip(DepartureDto d)
    {
        var noun = LineNoun.For(d.Line);
        return $"{noun.Article} {noun.Name} Richtung {d.Destination} {Minutes(d.Minutes)}";
    }

    // ── Time phrases ─────────────────────────────────────────────────────────

    /// <summary>0 → "in weniger als einer Minute", 1 → "in einer Minute", n → "in n Minuten".</summary>
    public static string Minutes(int minutes) => minutes switch
    {
        <= 0 => "in weniger als einer Minute",
        1    => "in einer Minute",
        _    => $"in {minutes} Minuten"
    };

    /// <summary>"in 3 und in 18 Minuten" (next two trips) or a single phrase if only one trip is known.</summary>
    public static string MinutesPair(IReadOnlyList<DepartureDto> trips)
    {
        if (trips.Count == 1)
            return Minutes(trips[0].Minutes);

        var (a, b) = (trips[0].Minutes, trips[1].Minutes);
        return a >= 2 && b >= 2
            ? $"in {a} und in {b} Minuten"
            : $"{Minutes(a)} und {Minutes(b)}";
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>Grammatical form of a line: numeric lines are masculine ("der 4er"), others "die Linie N5".</summary>
public sealed record LineNoun(string Article, string Name, string None)
{
    public static LineNoun For(string line) =>
        LineNames.IsNumeric(line)
            ? new LineNoun("der", $"{line}er", "keinen")
            : new LineNoun("die", $"Linie {line}", "keine");
}
