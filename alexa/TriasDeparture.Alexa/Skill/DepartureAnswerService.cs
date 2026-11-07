using TriasDeparture.Alexa.Speech;
using TriasDeparture.Application.Interfaces;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Alexa.Skill;

/// <summary>Fetches departures for a stop and turns them into the spoken answer.</summary>
public sealed class DepartureAnswerService(ITriasClient client)
{
    /// <param name="line">TRIAS line name ("3", "N5"), or null for any line.</param>
    /// <param name="direction">Spoken direction ("Liebenau", "Hauptbahnhof"), or null for all directions.</param>
    public async Task<string> AnswerAsync(ResolvedStop stop, string? line, string? direction, CancellationToken ct = default)
    {
        var departures = await client.GetDeparturesAsync(stop.Id, ct: ct);
        var candidates = ForLine(departures, line);

        var prefix = string.Empty;
        if (direction is not null && candidates.Count > 0)
        {
            var matched = DirectionMatcher.Filter(candidates, d => DirectionMatcher.MatchesDestination(d, direction));

            // Not a destination – maybe a stop along the way (onward calls are only fetched now, they're big)
            if (matched.Count == 0)
            {
                var withOnward = ForLine(await client.GetDeparturesAsync(stop.Id, includeOnwardStops: true, ct), line);
                matched = DirectionMatcher.Filter(withOnward, d => DirectionMatcher.MatchesOnwardStop(d, direction));
            }

            if (matched.Count > 0)
                candidates = matched;
            else
                prefix = AnswerFormatter.DirectionNotFound(direction, line) + " ";
        }

        var answer = line is null
            ? AnswerFormatter.AnyLineAnswer(stop.Name, candidates)
            : AnswerFormatter.LineAnswer(line, stop.Name, AnswerFormatter.GroupByDirection(candidates));

        return prefix + answer;
    }

    private static IReadOnlyList<DepartureDto> ForLine(IReadOnlyList<DepartureDto> departures, string? line) =>
        line is null
            ? departures
            : departures.Where(d => string.Equals(d.Line.Trim(), line, StringComparison.OrdinalIgnoreCase)).ToList();
}
