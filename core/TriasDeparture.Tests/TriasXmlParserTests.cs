using Shouldly;
using TriasDeparture.Infrastructure.Trias;

namespace TriasDeparture.Tests;

public sealed class TriasXmlParserTests
{
    private readonly TriasXmlParser _parser = new();

    // ──────────────────────────────────────────────────────────────────────────
    // LocationInformationResponse sample (TRIAS 1.2)
    // ──────────────────────────────────────────────────────────────────────────
    private const string LocationXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Trias version="1.2" xmlns="http://www.vdv.de/trias" xmlns:siri="http://www.siri.org.uk/siri">
  <ServiceDelivery>
    <siri:ResponseTimestamp>2026-10-07T12:00:00Z</siri:ResponseTimestamp>
    <DeliveryPayload>
      <LocationInformationResponse>
        <LocationResult>
          <Location>
            <StopPoint>
              <StopPointRef>at:46:5678</StopPointRef>
              <StopPointName><Text>Graz Jakominiplatz</Text></StopPointName>
            </StopPoint>
            <LocationName><Text>Graz</Text></LocationName>
          </Location>
          <Complete>true</Complete>
          <Probability>0.5</Probability>
        </LocationResult>
        <LocationResult>
          <Location>
            <StopPoint>
              <StopPointRef>at:46:1234</StopPointRef>
              <StopPointName><Text>Krenngasse</Text></StopPointName>
            </StopPoint>
            <LocationName><Text>Graz</Text></LocationName>
          </Location>
          <Complete>true</Complete>
          <Probability>0.9</Probability>
        </LocationResult>
        <LocationResult>
          <Location>
            <StopPlace>
              <StopPlaceRef>at:46:9999</StopPlaceRef>
              <StopPlaceName><Text>Hauptbahnhof</Text></StopPlaceName>
            </StopPlace>
          </Location>
          <Probability>0.1</Probability>
        </LocationResult>
      </LocationInformationResponse>
    </DeliveryPayload>
  </ServiceDelivery>
</Trias>
""";

    [Fact]
    public void ParseStops_ReturnsCorrectCount()
    {
        var stops = _parser.ParseStops(LocationXml);
        stops.Count.ShouldBe(3);
    }

    [Fact]
    public void ParseStops_ReturnsCorrectIds()
    {
        var stops = _parser.ParseStops(LocationXml);
        stops.ShouldContain(s => s.Id == "at:46:1234");
        stops.ShouldContain(s => s.Id == "at:46:5678");
        stops.ShouldContain(s => s.Id == "at:46:9999");
    }

    [Fact]
    public void ParseStops_CombinesLocalityAndStopName()
    {
        var stops = _parser.ParseStops(LocationXml);
        stops.ShouldContain(s => s.Name == "Graz Krenngasse");
        stops.ShouldContain(s => s.Name == "Graz Jakominiplatz"); // locality not duplicated
        stops.ShouldContain(s => s.Name == "Hauptbahnhof");
    }

    [Fact]
    public void ParseStops_OrdersByProbability()
    {
        var stops = _parser.ParseStops(LocationXml);
        stops.Select(s => s.Id).ShouldBe(["at:46:1234", "at:46:5678", "at:46:9999"]);
    }

    [Fact]
    public void ParseStops_EmptyXml_ReturnsEmpty()
    {
        const string xml = """
<?xml version="1.0" encoding="UTF-8"?>
<Trias version="1.2" xmlns="http://www.vdv.de/trias">
  <ServiceDelivery>
    <DeliveryPayload>
      <LocationInformationResponse />
    </DeliveryPayload>
  </ServiceDelivery>
</Trias>
""";
        var stops = _parser.ParseStops(xml);
        stops.ShouldBeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StopEventResponse helpers
    // ──────────────────────────────────────────────────────────────────────────
    private static string StopEvent(
        string plannedTime, string? estimatedTime = null,
        string line = "3", string mode = "tram", bool cancelled = false)
    {
        var estElement = estimatedTime is not null
            ? $"<EstimatedTime>{estimatedTime}</EstimatedTime>"
            : string.Empty;
        var cancelledElement = cancelled ? "<Cancelled>true</Cancelled>" : string.Empty;

        return $"""
        <StopEventResult>
          <StopEvent>
            <ThisCall>
              <CallAtStop>
                <ServiceDeparture>
                  <TimetabledTime>{plannedTime}</TimetabledTime>
                  {estElement}
                </ServiceDeparture>
              </CallAtStop>
            </ThisCall>
            <Service>
              <ServiceSection>
                <LineRef>stv:33003::R</LineRef>
                <Mode><PtMode>{mode}</PtMode></Mode>
                <PublishedLineName><Text>{line}</Text></PublishedLineName>
              </ServiceSection>
              <DestinationText><Text>Andritz</Text></DestinationText>
              {cancelledElement}
            </Service>
          </StopEvent>
        </StopEventResult>
""";
    }

    private static string StopEventXml(params string[] events) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<Trias version="1.2" xmlns="http://www.vdv.de/trias" xmlns:siri="http://www.siri.org.uk/siri">
  <ServiceDelivery>
    <DeliveryPayload>
      <StopEventResponse>
        {string.Join(Environment.NewLine, events)}
      </StopEventResponse>
    </DeliveryPayload>
  </ServiceDelivery>
</Trias>
""";

    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ParseDepartures_ReturnsOneDeparture()
    {
        var xml = StopEventXml(StopEvent("2026-10-07T12:05:00Z"));

        var departures = _parser.ParseDepartures(xml, Now);

        departures.Count.ShouldBe(1);
        departures[0].Line.ShouldBe("3");
        departures[0].Destination.ShouldBe("Andritz");
        departures[0].Mode.ShouldBe("tram");
        departures[0].Cancelled.ShouldBeFalse();
        departures[0].Minutes.ShouldBe(5);
    }

    [Fact]
    public void ParseDepartures_UsesEstimatedTimeForMinutes()
    {
        var xml = StopEventXml(StopEvent("2026-10-07T12:05:00Z", "2026-10-07T12:07:00Z"));

        var departures = _parser.ParseDepartures(xml, Now);

        departures[0].EstimatedTime.ShouldBe(new DateTime(2026, 10, 7, 12, 7, 0, DateTimeKind.Utc));
        departures[0].Minutes.ShouldBe(7);
    }

    [Fact]
    public void ParseDepartures_NormalizesOffsetTimesToUtc()
    {
        // 14:05 CEST == 12:05 UTC
        var xml = StopEventXml(StopEvent("2026-10-07T14:05:00+02:00"));

        var departures = _parser.ParseDepartures(xml, Now);

        departures[0].PlannedTime.ShouldBe(new DateTime(2026, 10, 7, 12, 5, 0, DateTimeKind.Utc));
        departures[0].PlannedTime.Kind.ShouldBe(DateTimeKind.Utc);
        departures[0].Minutes.ShouldBe(5);
    }

    [Fact]
    public void ParseDepartures_SortsByEffectiveTime()
    {
        var xml = StopEventXml(
            StopEvent("2026-10-07T12:02:00Z", "2026-10-07T12:10:00Z", line: "1"), // delayed
            StopEvent("2026-10-07T12:05:00Z", line: "63", mode: "bus"));

        var departures = _parser.ParseDepartures(xml, Now);

        departures.Select(d => d.Line).ShouldBe(["63", "1"]);
        departures[0].Mode.ShouldBe("bus");
    }

    [Fact]
    public void ParseDepartures_FlagsCancelledTrips()
    {
        var xml = StopEventXml(StopEvent("2026-10-07T12:05:00Z", cancelled: true));

        _parser.ParseDepartures(xml, Now)[0].Cancelled.ShouldBeTrue();
    }

    [Fact]
    public void ParseDepartures_SkipsEventsWithoutTimetabledTime()
    {
        var xml = StopEventXml(StopEvent("not-a-date"), StopEvent("2026-10-07T12:05:00Z"));

        _parser.ParseDepartures(xml, Now).Count.ShouldBe(1);
    }

    [Fact]
    public void ParseDepartures_EmptyXml_ReturnsEmpty()
    {
        var departures = _parser.ParseDepartures(StopEventXml(), Now);
        departures.ShouldBeEmpty();
    }
}
