using System.Globalization;

namespace TriasDeparture.Infrastructure.Trias;

/// <summary>
/// Builds TRIAS 1.2-compliant XML request bodies.
/// Namespace: http://www.vdv.de/trias
/// SIRI namespace: http://www.siri.org.uk/siri
/// </summary>
public sealed class TriasXmlBuilder
{
    private const string TriasNs = "http://www.vdv.de/trias";
    private const string SiriNs  = "http://www.siri.org.uk/siri";

    // Second precision with explicit "Z" – EFA-based TRIAS servers can reject 7-digit fractions
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private readonly string _requestorRef;

    public TriasXmlBuilder(string requestorRef)
    {
        _requestorRef = requestorRef;
    }

    /// <summary>Builds a LocationInformationRequest XML for stop search.</summary>
    public string BuildLocationInformationRequest(string query)
    {
        var timestamp = Format(DateTime.UtcNow);
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Trias version="1.2" xmlns="{TriasNs}" xmlns:siri="{SiriNs}">
    <ServiceRequest>
        <siri:RequestTimestamp>{timestamp}</siri:RequestTimestamp>
        <siri:RequestorRef>{EscapeXml(_requestorRef)}</siri:RequestorRef>
        <RequestPayload>
            <LocationInformationRequest>
                <InitialInput>
                    <LocationName>{EscapeXml(query)}</LocationName>
                </InitialInput>
                <Restrictions>
                    <Type>stop</Type>
                    <NumberOfResults>20</NumberOfResults>
                </Restrictions>
            </LocationInformationRequest>
        </RequestPayload>
    </ServiceRequest>
</Trias>
""";
    }

    /// <summary>Builds a StopEventRequest XML for live departures from now on.</summary>
    public string BuildStopEventRequest(string stopId, int numberOfResults = 20) =>
        BuildStopEventRequest(stopId, DateTime.UtcNow, numberOfResults);

    /// <summary>Builds a StopEventRequest XML for live departures from <paramref name="departureTimeUtc"/> on.</summary>
    public string BuildStopEventRequest(string stopId, DateTime departureTimeUtc, int numberOfResults = 20)
    {
        var timestamp = Format(DateTime.UtcNow);
        var depTime   = Format(departureTimeUtc);
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Trias version="1.2" xmlns="{TriasNs}" xmlns:siri="{SiriNs}">
    <ServiceRequest>
        <siri:RequestTimestamp>{timestamp}</siri:RequestTimestamp>
        <siri:RequestorRef>{EscapeXml(_requestorRef)}</siri:RequestorRef>
        <RequestPayload>
            <StopEventRequest>
                <Location>
                    <LocationRef>
                        <StopPointRef>{EscapeXml(stopId)}</StopPointRef>
                    </LocationRef>
                    <DepArrTime>{depTime}</DepArrTime>
                </Location>
                <Params>
                    <NumberOfResults>{numberOfResults}</NumberOfResults>
                    <StopEventType>departure</StopEventType>
                    <IncludeRealtimeData>true</IncludeRealtimeData>
                </Params>
            </StopEventRequest>
        </RequestPayload>
    </ServiceRequest>
</Trias>
""";
    }

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
