using System.Text.Json.Serialization;

namespace TriasDeparture.Domain.Models;

/// <summary>Represents a single departure at a stop. All times are UTC.</summary>
/// <param name="Mode">TRIAS PtMode, e.g. "tram" or "bus" (null if not provided).</param>
/// <param name="Cancelled">True if the trip or this stop was cancelled.</param>
/// <param name="Direction">TRIAS DirectionRef of the line, "outward" or "inward" (null if not provided).</param>
/// <param name="OnwardStops">Names of the stops the trip calls at after this one (only filled when requested).</param>
public record DepartureDto(
    string Line,
    string Destination,
    int Minutes,
    DateTime PlannedTime,
    DateTime? EstimatedTime,
    string? Mode = null,
    bool Cancelled = false,
    string? Direction = null,
    [property: JsonIgnore] IReadOnlyList<string>? OnwardStops = null)
{
    /// <summary>Realtime departure time if available, otherwise the timetabled one.</summary>
    [JsonIgnore]
    public DateTime EffectiveTime => EstimatedTime ?? PlannedTime;

    /// <summary>Whole minutes from <paramref name="nowUtc"/> until departure (never negative).</summary>
    public int MinutesUntilDeparture(DateTime nowUtc) =>
        (int)Math.Max(0, Math.Round((EffectiveTime - nowUtc).TotalMinutes));
}
