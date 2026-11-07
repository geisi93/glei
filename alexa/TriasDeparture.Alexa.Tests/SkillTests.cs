using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TriasDeparture.Alexa.HomeStops;
using TriasDeparture.Alexa.Models;
using TriasDeparture.Alexa.Skill;
using TriasDeparture.Domain.Models;
using static TriasDeparture.Alexa.Tests.TestData;

namespace TriasDeparture.Alexa.Tests;

public sealed class SkillTests
{
    private const string Krenngasse        = "at:46:3171";
    private const string Reininghausstrasse = "at:46:30263";

    private readonly FakeTriasClient _trias = new();
    private readonly InMemoryHomeStopStore _homeStops = new();

    private SkillHandler Handler => new(
        new SlotResolver(_trias), new DepartureAnswerService(_trias), _homeStops, NullLogger<SkillHandler>.Instance);

    private async Task<SkillResponse> Ask(SkillRequest request) => await Handler.HandleAsync(request);

    private static string Speech(SkillResponse r) => r.Response.OutputSpeech!.Text;

    public SkillTests()
    {
        _trias.Departures[Krenngasse] = [Dep("3", 3), Dep("5", 4, "Puntigam"), Dep("3", 11)];
        _trias.Departures[Reininghausstrasse] =
        [
            Dep("4", 3, "Liebenau", "outward"), Dep("4", 7, "Reininghaus", "inward"),
            Dep("4", 18, "Liebenau", "outward"), Dep("4", 22, "Reininghaus", "inward")
        ];
        _trias.DeparturesWithOnward[Reininghausstrasse] =
        [
            Dep("4", 3, "Liebenau", "outward", false, "Alte Poststraße", "Graz Hauptbahnhof (Vorplatz-Tiefgeschoß)"),
            Dep("4", 7, "Reininghaus", "inward", false, "Reininghauspark/tim", "Reininghaus"),
            Dep("4", 18, "Liebenau", "outward", false, "Alte Poststraße", "Graz Hauptbahnhof (Vorplatz-Tiefgeschoß)")
        ];
    }

    // ── Departures ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NextDeparture_LineAndStop()
    {
        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("line", "dreier", "3"), Slot("stop", "Krenngasse", Krenngasse)]));

        Speech(response).ShouldBe("Der nächste 3er Richtung Andritz fährt in 3 Minuten, der übernächste in 11 Minuten.");
        response.Response.ShouldEndSession.ShouldBe(true);
    }

    [Fact]
    public async Task NextDeparture_BothDirections_WithoutDirection()
    {
        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("line", "vierer", "4"), Slot("stop", "Reininghausstraße", Reininghausstrasse)]));

        Speech(response).ShouldBe(
            "Der 4er Richtung Liebenau fährt in 3 und in 18 Minuten, Richtung Reininghaus in 7 und in 22 Minuten.");
        _trias.OnwardRequests.ShouldBe(0);
    }

    [Fact]
    public async Task NextDeparture_DirectionMatchesDestination_NoOnwardRequest()
    {
        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("line", "vierer", "4"), Slot("stop", "Reininghausstraße", Reininghausstrasse), Slot("direction", "Liebenau")]));

        Speech(response).ShouldBe("Der nächste 4er Richtung Liebenau fährt in 3 Minuten, der übernächste in 18 Minuten.");
        _trias.OnwardRequests.ShouldBe(0);
    }

    [Fact]
    public async Task NextDeparture_DirectionMatchesOnwardStop()
    {
        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("line", "vierer", "4"), Slot("stop", "Reininghausstraße", Reininghausstrasse), Slot("direction", "Hauptbahnhof")]));

        Speech(response).ShouldBe("Der nächste 4er Richtung Liebenau fährt in 3 Minuten, der übernächste in 18 Minuten.");
        _trias.OnwardRequests.ShouldBe(1);
    }

    [Fact]
    public async Task NextDeparture_UnknownDirection_AnswersAllDirections()
    {
        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("line", "vierer", "4"), Slot("stop", "Reininghausstraße", Reininghausstrasse), Slot("direction", "Webling")]));

        Speech(response).ShouldStartWith("Richtung Webling habe ich keinen 4er gefunden. Der 4er Richtung Liebenau");
    }

    [Fact]
    public async Task NextDeparture_WithoutLine()
    {
        var response = await Ask(Intent("NextDepartureIntent", [Slot("stop", "Krenngasse", Krenngasse)]));

        Speech(response).ShouldBe("An der Haltestelle Krenngasse fährt als nächstes der 3er Richtung Andritz in 3 Minuten, " +
                                  "danach der 5er Richtung Puntigam in 4 Minuten.");
    }

    [Fact]
    public async Task NextDeparture_UnknownStop()
    {
        _trias.Stops.Add(new StopDto("at:20:1", "Klagenfurt Morogasse")); // fuzzy TRIAS hit, must be ignored

        var response = await Ask(Intent("NextDepartureIntent", [Slot("line", "dreier", "3"), Slot("stop", "Nirgendwogasse")]));

        Speech(response).ShouldBe("Die Haltestelle Nirgendwogasse kenne ich nicht.");
    }

    [Fact]
    public async Task NextDeparture_TriasDown()
    {
        _trias.Failure = new HttpRequestException("down");

        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("line", "dreier", "3"), Slot("stop", "Krenngasse", Krenngasse)]));

        Speech(response).ShouldBe(SkillHandler.Unavailable);
    }

    // ── Missing stop: home stop or follow-up question ────────────────────────

    [Fact]
    public async Task NextDeparture_NoStop_NoHomeStop_AsksAndRemembersLine()
    {
        var response = await Ask(Intent("NextDepartureIntent", [Slot("line", "vierer", "4"), Slot("direction", "Liebenau")]));

        Speech(response).ShouldBe(SkillHandler.AskStop);
        response.Response.ShouldEndSession.ShouldBe(false);
        response.SessionAttributes!["line"].ShouldBe("4");
        response.SessionAttributes!["direction"].ShouldBe("Liebenau");
    }

    [Fact]
    public async Task NextDeparture_FollowUpWithStopOnly_UsesRememberedLineAndDirection()
    {
        var response = await Ask(Intent("NextDepartureIntent",
            [Slot("stop", "Reininghausstraße", Reininghausstrasse)],
            new Dictionary<string, string> { ["awaiting"] = "departureStop", ["line"] = "4", ["direction"] = "Liebenau" }));

        Speech(response).ShouldBe("Der nächste 4er Richtung Liebenau fährt in 3 Minuten, der übernächste in 18 Minuten.");
    }

    [Fact]
    public async Task NextDeparture_NoStop_UsesHomeStop()
    {
        await _homeStops.SetAsync("amzn1.ask.account.TEST", new ResolvedStop(Krenngasse, "Krenngasse"));

        var response = await Ask(Intent("NextDepartureIntent", [Slot("line", "dreier", "3")]));

        Speech(response).ShouldBe("Der nächste 3er Richtung Andritz fährt in 3 Minuten, der übernächste in 11 Minuten.");
    }

    [Fact]
    public async Task HomeStop_SetAndGet()
    {
        Speech(await Ask(Intent("GetHomeStopIntent"))).ShouldStartWith("Du hast noch keine Haltestelle gespeichert.");

        Speech(await Ask(Intent("SetHomeStopIntent", [Slot("stop", "Krenngasse", Krenngasse)])))
            .ShouldBe("Alles klar, ich habe Krenngasse als deine Haltestelle gespeichert.");

        Speech(await Ask(Intent("GetHomeStopIntent"))).ShouldBe("Deine Haltestelle ist Krenngasse.");
        Speech(await Ask(Intent("GetHomeStopIntent", userId: "someone-else"))).ShouldStartWith("Du hast noch keine");
    }

    [Fact]
    public async Task HomeStop_WithoutStop_AsksThenSavesFollowUp()
    {
        var question = await Ask(Intent("SetHomeStopIntent"));
        Speech(question).ShouldBe(SkillHandler.AskHomeStop);

        // The answer "Krenngasse" matches the NextDepartureIntent utterance "{stop}"
        var saved = await Ask(Intent("NextDepartureIntent", [Slot("stop", "Krenngasse", Krenngasse)],
            question.SessionAttributes));

        Speech(saved).ShouldBe("Alles klar, ich habe Krenngasse als deine Haltestelle gespeichert.");
    }

    // ── Other requests ───────────────────────────────────────────────────────

    [Fact]
    public async Task Launch_KeepsSessionOpen()
    {
        var response = await Ask(Request(new RequestBody("LaunchRequest", "req", Now, "de-DE")));

        Speech(response).ShouldBe(SkillHandler.Welcome);
        response.Response.ShouldEndSession.ShouldBe(false);
        response.Response.Reprompt.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("AMAZON.StopIntent")]
    [InlineData("AMAZON.CancelIntent")]
    [InlineData("AMAZON.NavigateHomeIntent")]
    public async Task StopIntents_SayGoodbye(string intent) =>
        Speech(await Ask(Intent(intent))).ShouldBe(SkillHandler.Goodbye);

    [Theory]
    [InlineData("AMAZON.HelpIntent")]
    [InlineData("AMAZON.FallbackIntent")]
    public async Task HelpAndFallback_ExplainUsage(string intent) =>
        Speech(await Ask(Intent(intent))).ShouldBe(SkillHandler.Welcome);

    [Fact]
    public async Task SessionEnded_ReturnsEmptyResponse()
    {
        var response = await Ask(Request(new RequestBody("SessionEndedRequest", "req", Now, "de-DE", Reason: "USER_INITIATED")));

        response.Response.OutputSpeech.ShouldBeNull();
    }

    // ── Slot resolution ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveStop_PrefersExactNameAmongSeveralMatches()
    {
        var slot = SlotWithMatches("stop", "Griesplatz",
            ("Griesplatz Rösselmühlgasse", "at:46:1"), ("Griesplatz", "at:46:2"));

        var result = await new SlotResolver(_trias).ResolveStopAsync(slot);

        result.Stop.ShouldBe(new ResolvedStop("at:46:2", "Griesplatz"));
    }

    [Fact]
    public async Task ResolveStop_FallsBackToSearch_PreferringGraz()
    {
        _trias.Stops.AddRange([new StopDto("at:46:9", "Leibnitz Hauptplatz"), new StopDto("at:46:8", "Graz Hauptplatz")]);

        var result = await new SlotResolver(_trias).ResolveStopAsync(Slot("stop", "Hauptplatz"));

        result.Stop.ShouldBe(new ResolvedStop("at:46:8", "Hauptplatz"));
    }

    [Fact]
    public void ResolveLine_FromIdOrWords()
    {
        SlotResolver.ResolveLine(Slot("line", "vierundsechziger e", "64E")).ShouldBe("64E");
        SlotResolver.ResolveLine(Slot("line", "Dreier")).ShouldBe("3");
        SlotResolver.ResolveLine(null).ShouldBeNull();
    }

    // ── JSON format ──────────────────────────────────────────────────────────

    [Fact]
    public void Request_DeserializesAlexaJson()
    {
        const string json = """
        {
          "version": "1.0",
          "session": {
            "new": false, "sessionId": "amzn1.echo-api.session.1",
            "application": { "applicationId": "amzn1.ask.skill.abc" },
            "attributes": { "awaiting": "departureStop", "line": "4" },
            "user": { "userId": "amzn1.ask.account.XYZ" }
          },
          "context": {
            "System": {
              "application": { "applicationId": "amzn1.ask.skill.abc" },
              "user": { "userId": "amzn1.ask.account.XYZ" },
              "device": { "deviceId": "d1", "supportedInterfaces": {} },
              "apiEndpoint": "https://api.eu.amazonalexa.com"
            }
          },
          "request": {
            "type": "IntentRequest", "requestId": "amzn1.echo-api.request.1",
            "locale": "de-DE", "timestamp": "2026-11-07T12:00:00Z",
            "intent": {
              "name": "NextDepartureIntent", "confirmationStatus": "NONE",
              "slots": {
                "stop": {
                  "name": "stop", "value": "krenngasse", "confirmationStatus": "NONE", "source": "USER",
                  "resolutions": { "resolutionsPerAuthority": [ {
                    "authority": "amzn1.er-authority.echo-sdk.amzn1.ask.skill.abc.STOP",
                    "status": { "code": "ER_SUCCESS_MATCH" },
                    "values": [ { "value": { "name": "Krenngasse", "id": "at:46:3171" } } ] } ] }
                },
                "line": { "name": "line", "confirmationStatus": "NONE" }
              }
            }
          }
        }
        """;

        var request = JsonSerializer.Deserialize<SkillRequest>(json, AlexaJson.Options)!;

        request.ApplicationId.ShouldBe("amzn1.ask.skill.abc");
        request.UserId.ShouldBe("amzn1.ask.account.XYZ");
        request.Request.Timestamp.ShouldBe(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
        request.Request.Intent!.Slots!["stop"].Resolutions!.ResolutionsPerAuthority![0].Values![0].Value.Id.ShouldBe("at:46:3171");
        request.Request.Intent.Slots["line"].Value.ShouldBeNull();
        request.Session!.Attributes!["line"].GetString().ShouldBe("4");
    }

    [Fact]
    public void Response_SerializesAlexaJson()
    {
        var json = JsonSerializer.Serialize(SkillResponse.Ask("Frage?", "Nochmal?", new() { ["line"] = "3" }), AlexaJson.Options);

        json.ShouldContain("\"version\":\"1.0\"");
        json.ShouldContain("\"outputSpeech\":{\"type\":\"PlainText\",\"text\":\"Frage?\"}");
        json.ShouldContain("\"shouldEndSession\":false");
        json.ShouldContain("\"sessionAttributes\":{\"line\":\"3\"}");

        JsonSerializer.Serialize(SkillResponse.Empty(), AlexaJson.Options).ShouldNotContain("shouldEndSession");
    }
}
