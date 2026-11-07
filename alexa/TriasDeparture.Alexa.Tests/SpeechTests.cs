using Shouldly;
using TriasDeparture.Alexa.Speech;
using static TriasDeparture.Alexa.Tests.TestData;

namespace TriasDeparture.Alexa.Tests;

public sealed class SpeechTests
{
    // ── Time phrases ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "in weniger als einer Minute")]
    [InlineData(1, "in einer Minute")]
    [InlineData(7, "in 7 Minuten")]
    public void Minutes_IsNaturalGerman(int minutes, string expected) =>
        AnswerFormatter.Minutes(minutes).ShouldBe(expected);

    [Fact]
    public void MinutesPair_CombinesTwoTrips()
    {
        AnswerFormatter.MinutesPair([Dep("4", 3), Dep("4", 18)]).ShouldBe("in 3 und in 18 Minuten");
        AnswerFormatter.MinutesPair([Dep("4", 0), Dep("4", 8)]).ShouldBe("in weniger als einer Minute und in 8 Minuten");
        AnswerFormatter.MinutesPair([Dep("4", 5)]).ShouldBe("in 5 Minuten");
    }

    // ── Line answers ─────────────────────────────────────────────────────────

    [Fact]
    public void LineAnswer_OneDirection_NextAndSecondNext()
    {
        var groups = AnswerFormatter.GroupByDirection([Dep("4", 3, "Liebenau"), Dep("4", 18, "Liebenau")]);

        AnswerFormatter.LineAnswer("4", "Reininghausstraße", groups)
            .ShouldBe("Der nächste 4er Richtung Liebenau fährt in 3 Minuten, der übernächste in 18 Minuten.");
    }

    [Fact]
    public void LineAnswer_OneDirection_OnlyOneTrip()
    {
        var groups = AnswerFormatter.GroupByDirection([Dep("4", 3, "Liebenau")]);

        AnswerFormatter.LineAnswer("4", "Reininghausstraße", groups)
            .ShouldBe("Der nächste 4er Richtung Liebenau fährt in 3 Minuten. Eine weitere Abfahrt habe ich nicht gefunden.");
    }

    [Fact]
    public void LineAnswer_BothDirections()
    {
        var groups = AnswerFormatter.GroupByDirection([
            Dep("4", 3, "Liebenau", "outward"), Dep("4", 7, "Reininghaus", "inward"),
            Dep("4", 18, "Liebenau", "outward"), Dep("4", 22, "Reininghaus", "inward")]);

        AnswerFormatter.LineAnswer("4", "Reininghausstraße", groups)
            .ShouldBe("Der 4er Richtung Liebenau fährt in 3 und in 18 Minuten, Richtung Reininghaus in 7 und in 22 Minuten.");
    }

    [Fact]
    public void LineAnswer_NoTrips()
    {
        AnswerFormatter.LineAnswer("4", "Krenngasse", [])
            .ShouldBe("An der Haltestelle Krenngasse habe ich keinen 4er mehr gefunden.");
    }

    [Fact]
    public void LineAnswer_NonNumericLine_UsesLinie()
    {
        var groups = AnswerFormatter.GroupByDirection([Dep("N5", 4, "Puntigam"), Dep("N5", 34, "Puntigam")]);

        AnswerFormatter.LineAnswer("N5", "Jakominiplatz", groups)
            .ShouldBe("Die nächste Linie N5 Richtung Puntigam fährt in 4 Minuten, die übernächste in 34 Minuten.");
        AnswerFormatter.LineAnswer("N5", "Jakominiplatz", [])
            .ShouldBe("An der Haltestelle Jakominiplatz habe ich keine Linie N5 mehr gefunden.");
    }

    [Fact]
    public void GroupByDirection_SkipsCancelled_AndFallsBackToDestination()
    {
        var groups = AnswerFormatter.GroupByDirection([
            Dep("3", 1, cancelled: true), Dep("3", 4, "Andritz", direction: null),
            Dep("3", 6, "Krenngasse", direction: null)]);

        groups.Select(g => g.Destination).ShouldBe(["Andritz", "Krenngasse"]);
        groups[0].Trips.Single().Minutes.ShouldBe(4);
    }

    [Fact]
    public void GroupByDirection_KeepsShortRunsInTheirDirection()
    {
        // Same DirectionRef, different destination → one group, labelled with the next trip's destination
        var groups = AnswerFormatter.GroupByDirection([
            Dep("4", 2, "Jakominiplatz", "outward"), Dep("4", 9, "Liebenau", "outward")]);

        groups.Count.ShouldBe(1);
        groups[0].Destination.ShouldBe("Jakominiplatz");
        groups[0].Trips.Count.ShouldBe(2);
    }

    [Fact]
    public void AnyLineAnswer_NextTwoOfAllLines()
    {
        AnswerFormatter.AnyLineAnswer("Krenngasse", [Dep("5", 4, "Puntigam"), Dep("3", 2), Dep("3", 12)])
            .ShouldBe("An der Haltestelle Krenngasse fährt als nächstes der 3er Richtung Andritz in 2 Minuten, " +
                      "danach der 5er Richtung Puntigam in 4 Minuten.");
        AnswerFormatter.AnyLineAnswer("Krenngasse", [])
            .ShouldBe("An der Haltestelle Krenngasse habe ich keine Abfahrten mehr gefunden.");
    }

    [Fact]
    public void DirectionNotFound_MentionsLine()
    {
        AnswerFormatter.DirectionNotFound("Webling", "4").ShouldBe("Richtung Webling habe ich keinen 4er gefunden.");
        AnswerFormatter.DirectionNotFound("Webling", null).ShouldBe("Richtung Webling habe ich keine Abfahrt gefunden.");
    }

    // ── Numbers, line names, normalization ───────────────────────────────────

    [Theory]
    [InlineData(1, "eins", "einser")]
    [InlineData(3, "drei", "dreier")]
    [InlineData(7, "sieben", "siebener")]
    [InlineData(16, "sechzehn", "sechzehner")]
    [InlineData(21, "einundzwanzig", "einundzwanziger")]
    [InlineData(30, "dreißig", "dreißiger")]
    [InlineData(64, "vierundsechzig", "vierundsechziger")]
    public void GermanNumbers_Words(int n, string cardinal, string colloquial)
    {
        GermanNumbers.Cardinal(n).ShouldBe(cardinal);
        GermanNumbers.Colloquial(n).ShouldBe(colloquial);
        GermanNumbers.TryParse(colloquial.ToUpperInvariant(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(n);
    }

    [Theory]
    [InlineData("Dreier", "3")]
    [InlineData("3er", "3")]
    [InlineData("linie 3", "3")]
    [InlineData("Linie 64 E", "64E")]
    [InlineData("vierundsechzig e", "64E")]
    [InlineData("N5", "N5")]
    [InlineData("n fünf", "N5")]
    [InlineData("die bim", null)]
    [InlineData("hallo", null)]
    public void LineNames_Parse(string spoken, string? expected) =>
        LineNames.Parse(spoken).ShouldBe(expected);

    [Theory]
    [InlineData("Liebenau", "Liebenau Murpark", true)]
    [InlineData("hauptbahnhof", "Graz Hauptbahnhof (Vorplatz-Tiefgeschoß)", true)]
    [InlineData("St. Leonhard", "Sankt Leonhard/Klinikum", true)]
    [InlineData("Gösting", "Goesting", true)]
    [InlineData("Lend", "Lendplatz", false)]
    [InlineData("", "Andritz", false)]
    public void TextNormalizer_SimilarTo(string a, string b, bool expected) =>
        TextNormalizer.SimilarTo(a, b).ShouldBe(expected);

    [Fact]
    public void DirectionMatcher_Filter_KeepsWholeDirection()
    {
        IReadOnlyList<Domain.Models.DepartureDto> departures =
        [
            Dep("4", 2, "Jakominiplatz", "outward"), Dep("4", 5, "Reininghaus", "inward"), Dep("4", 9, "Liebenau", "outward")
        ];

        var matched = DirectionMatcher.Filter(departures, d => DirectionMatcher.MatchesDestination(d, "Liebenau"));

        matched.Select(d => d.Minutes).ShouldBe([2, 9]);
    }
}
