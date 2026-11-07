using Shouldly;
using TriasDeparture.Infrastructure.Trias;

namespace TriasDeparture.Tests;

public sealed class TriasXmlBuilderTests
{
    private readonly TriasXmlBuilder _builder = new("TEST_REQUESTOR");

    [Fact]
    public void BuildLocationInformationRequest_ContainsQuery()
    {
        var xml = _builder.BuildLocationInformationRequest("Krenngasse");

        xml.ShouldContain("<LocationName>Krenngasse</LocationName>");
        xml.ShouldContain("TEST_REQUESTOR");
        xml.ShouldContain("LocationInformationRequest");
    }

    [Fact]
    public void BuildLocationInformationRequest_EscapesSpecialChars()
    {
        var xml = _builder.BuildLocationInformationRequest("A&B");

        xml.ShouldContain("A&amp;B");
        xml.ShouldNotContain("<LocationName>A&B</LocationName>");
    }

    [Fact]
    public void BuildStopEventRequest_ContainsStopId()
    {
        var xml = _builder.BuildStopEventRequest("de:643:1234");

        xml.ShouldContain("<StopPointRef>de:643:1234</StopPointRef>");
        xml.ShouldContain("StopEventRequest");
        xml.ShouldContain("departure");
    }

    [Fact]
    public void BuildStopEventRequest_RequestsRealtimeData()
    {
        // TRIAS defaults IncludeRealtimeData to false – without it there are no EstimatedTimes
        var xml = _builder.BuildStopEventRequest("at:46:1");

        xml.ShouldContain("<IncludeRealtimeData>true</IncludeRealtimeData>");
    }

    [Fact]
    public void BuildStopEventRequest_UsesGivenDepartureTimeInUtc()
    {
        var xml = _builder.BuildStopEventRequest(
            "at:46:1", new DateTime(2026, 10, 7, 12, 5, 30, DateTimeKind.Utc), 50);

        xml.ShouldContain("<DepArrTime>2026-10-07T12:05:30Z</DepArrTime>");
        xml.ShouldContain("<NumberOfResults>50</NumberOfResults>");
    }

    [Fact]
    public void BuildStopEventRequest_OnwardCalls_OffByDefault()
    {
        var xml = _builder.BuildStopEventRequest("at:46:1");

        xml.ShouldContain("<IncludeOnwardCalls>false</IncludeOnwardCalls>");
    }

    [Fact]
    public void BuildStopEventRequest_OnwardCalls_WhenRequested()
    {
        var xml = _builder.BuildStopEventRequest("at:46:1", DateTime.UtcNow, 20, includeOnwardCalls: true);

        xml.ShouldContain("<IncludeOnwardCalls>true</IncludeOnwardCalls>");
    }

    [Fact]
    public void BuildStopEventRequest_IsValidXml()
    {
        var xml = _builder.BuildStopEventRequest("de:643:1234");

        Should.NotThrow(() => System.Xml.Linq.XDocument.Parse(xml));
    }

    [Fact]
    public void BuildLocationInformationRequest_IsValidXml()
    {
        var xml = _builder.BuildLocationInformationRequest("Jakominiplatz");

        Should.NotThrow(() => System.Xml.Linq.XDocument.Parse(xml));
    }
}
