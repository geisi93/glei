using TriasDeparture.Domain.Models;

namespace TriasDeparture.Alexa.Speech;

/// <summary>Matches a spoken direction ("Richtung Liebenau", "Richtung Hauptbahnhof") against trips.</summary>
public static class DirectionMatcher
{
    /// <summary>Key that identifies the direction of a trip within its line.</summary>
    public static string DirectionKey(DepartureDto d) => $"{d.Line}|{d.Direction ?? d.Destination}";

    public static bool MatchesDestination(DepartureDto d, string direction) =>
        TextNormalizer.SimilarTo(d.Destination, direction);

    /// <summary>True if the trip calls at a stop matching <paramref name="direction"/> later on (needs <see cref="DepartureDto.OnwardStops"/>).</summary>
    public static bool MatchesOnwardStop(DepartureDto d, string direction) =>
        d.OnwardStops?.Any(stop => TextNormalizer.SimilarTo(stop, direction)) == true;

    /// <summary>
    /// All trips whose direction matches: a direction counts as matched as soon as one of its trips matches,
    /// so short runs with a different destination in the same direction are kept.
    /// </summary>
    public static IReadOnlyList<DepartureDto> Filter(
        IReadOnlyList<DepartureDto> departures, Func<DepartureDto, bool> matches)
    {
        var keys = departures.Where(matches).Select(DirectionKey).ToHashSet();
        return departures.Where(d => keys.Contains(DirectionKey(d))).ToList();
    }
}
