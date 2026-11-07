using System.Globalization;
using System.Xml.Linq;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Infrastructure.Trias;

/// <summary>
/// Parses TRIAS 1.2 XML responses using LINQ to XML.
/// </summary>
public sealed class TriasXmlParser
{
    private static readonly XNamespace Ns = "http://www.vdv.de/trias";

    // ────────────────────────────────────────────────────────────────
    // LocationInformation
    // ────────────────────────────────────────────────────────────────

    /// <summary>Extracts stops from a LocationInformationResponse, most probable first.</summary>
    public IReadOnlyList<StopDto> ParseStops(string xml)
    {
        var doc = XDocument.Parse(xml);

        // Trias > ServiceDelivery > DeliveryPayload > LocationInformationResponse > LocationResult > Location
        return doc.Descendants(Ns + "LocationResult")
            .Select(result => new
            {
                Location    = result.Element(Ns + "Location"),
                Probability = ParseDouble(result.Element(Ns + "Probability")?.Value)
            })
            .Where(r => r.Location is not null)
            .OrderByDescending(r => r.Probability)
            .Select(r => ParseStop(r.Location!))
            .OfType<StopDto>()
            .DistinctBy(s => s.Id)
            .ToList()
            .AsReadOnly();
    }

    private static StopDto? ParseStop(XElement location)
    {
        // Stop searches return either a StopPoint or a StopPlace
        var stopPoint = location.Element(Ns + "StopPoint");
        var stopPlace = location.Element(Ns + "StopPlace");

        var id = stopPoint?.Element(Ns + "StopPointRef")?.Value
              ?? stopPlace?.Element(Ns + "StopPlaceRef")?.Value;
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var stopName = Text(stopPoint?.Element(Ns + "StopPointName"))
                    ?? Text(stopPlace?.Element(Ns + "StopPlaceName"))
                    ?? string.Empty;
        var locality = Text(location.Element(Ns + "LocationName"));

        // Combine locality and stop name ("Graz" + "Krenngasse" → "Graz Krenngasse")
        var name = locality is not null
                && !stopName.Contains(locality, StringComparison.OrdinalIgnoreCase)
            ? $"{locality} {stopName}".Trim()
            : stopName;

        return new StopDto(id, name);
    }

    // ────────────────────────────────────────────────────────────────
    // StopEvent
    // ────────────────────────────────────────────────────────────────

    /// <summary>Extracts departures from a StopEventResponse, sorted by effective departure time.</summary>
    public IReadOnlyList<DepartureDto> ParseDepartures(string xml) =>
        ParseDepartures(xml, DateTime.UtcNow);

    /// <inheritdoc cref="ParseDepartures(string)"/>
    public IReadOnlyList<DepartureDto> ParseDepartures(string xml, DateTime nowUtc)
    {
        var doc     = XDocument.Parse(xml);
        var results = new List<DepartureDto>();

        // Trias > ServiceDelivery > DeliveryPayload > StopEventResponse > StopEventResult > StopEvent
        foreach (var stopEvent in doc.Descendants(Ns + "StopEventResult").Elements(Ns + "StopEvent"))
        {
            var thisCall = stopEvent.Element(Ns + "ThisCall")?.Element(Ns + "CallAtStop");
            if (thisCall is null) continue;

            var serviceDeparture = thisCall.Element(Ns + "ServiceDeparture");

            // Planned departure time
            if (ParseUtc(serviceDeparture?.Element(Ns + "TimetabledTime")?.Value) is not { } plannedTime)
                continue;

            // Estimated departure time (realtime)
            var estimatedTime = ParseUtc(serviceDeparture?.Element(Ns + "EstimatedTime")?.Value);

            var service = stopEvent.Element(Ns + "Service");

            // Line and mode live in Service > ServiceSection (TRIAS 1.2)
            var section = service?.Element(Ns + "ServiceSection");

            var lineName = Text(section?.Element(Ns + "PublishedLineName"))
                        ?? section?.Element(Ns + "LineRef")?.Value
                        ?? string.Empty;

            var destination = Text(service?.Element(Ns + "DestinationText"))
                           ?? service?.Element(Ns + "DestinationStopPointRef")?.Value
                           ?? string.Empty;

            // e.g. "tram", "bus", "rail"
            var mode = section?.Element(Ns + "Mode")?.Element(Ns + "PtMode")?.Value;

            var cancelled = string.Equals(service?.Element(Ns + "Cancelled")?.Value, "true",
                                          StringComparison.OrdinalIgnoreCase)
                         || string.Equals(thisCall.Element(Ns + "NotServicedStop")?.Value, "true",
                                          StringComparison.OrdinalIgnoreCase);

            var departure = new DepartureDto(lineName, destination, 0, plannedTime, estimatedTime, mode, cancelled);
            results.Add(departure with { Minutes = departure.MinutesUntilDeparture(nowUtc) });
        }

        return results
            .OrderBy(d => d.EffectiveTime)
            .ToList()
            .AsReadOnly();
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>TRIAS wraps localized strings in a &lt;Text&gt; child element.</summary>
    private static string? Text(XElement? element)
    {
        var value = element?.Element(Ns + "Text")?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Parses an xs:dateTime into UTC. Handles "Z", explicit offsets ("+02:00") and
    /// offset-less values (assumed UTC), independent of the server's time zone.
    /// </summary>
    private static DateTime? ParseUtc(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.UtcDateTime
            : null;

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
}
