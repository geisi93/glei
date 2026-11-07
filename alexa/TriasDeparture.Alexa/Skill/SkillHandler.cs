using System.Text.Json;
using System.Xml;
using TriasDeparture.Alexa.HomeStops;
using TriasDeparture.Alexa.Models;

namespace TriasDeparture.Alexa.Skill;

/// <summary>Routes Alexa requests to the matching intent logic and builds the response.</summary>
public sealed class SkillHandler(
    SlotResolver slots,
    DepartureAnswerService answers,
    IHomeStopStore homeStops,
    ILogger<SkillHandler> logger)
{
    // ── Texts ────────────────────────────────────────────────────────────────

    internal const string Welcome =
        "Hallo, hier ist glei. Frag mich zum Beispiel: Wann fährt der nächste Dreier in der Krenngasse? " +
        "Du kannst auch deine Haltestelle speichern. Sag dazu: Meine Haltestelle ist Krenngasse.";
    internal const string Reprompt     = "Welche Linie und Haltestelle interessiert dich?";
    internal const string AskStop      = "Für welche Haltestelle?";
    internal const string AskStopAgain = "Für welche Haltestelle möchtest du die Abfahrten wissen?";
    internal const string AskHomeStop  = "Welche Haltestelle soll ich als deine Haltestelle speichern?";
    internal const string Goodbye      = "Bis bald!";
    internal const string Unavailable  = "Die Fahrplanauskunft ist gerade nicht erreichbar. Bitte versuch es gleich noch einmal.";

    // ── Session attributes (remember line/direction while asking for the stop) ──

    private const string AttrAwaiting   = "awaiting";
    private const string AttrLine       = "line";
    private const string AttrDirection  = "direction";
    private const string AwaitDeparture = "departureStop";
    private const string AwaitHomeStop  = "homeStop";

    public async Task<SkillResponse> HandleAsync(SkillRequest request, CancellationToken ct = default)
    {
        try
        {
            return request.Request.Type switch
            {
                "LaunchRequest"       => SkillResponse.Ask(Welcome, Reprompt),
                "IntentRequest"       => await HandleIntentAsync(request, ct),
                "SessionEndedRequest" => SkillResponse.Empty(),
                _                     => SkillResponse.Empty()
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "TRIAS request failed");
            return SkillResponse.Tell(Unavailable);
        }
    }

    private Task<SkillResponse> HandleIntentAsync(SkillRequest request, CancellationToken ct) =>
        request.Request.Intent?.Name switch
        {
            "NextDepartureIntent" => NextDepartureAsync(request, ct),
            "SetHomeStopIntent"   => SetHomeStopAsync(request, ct),
            "GetHomeStopIntent"   => GetHomeStopAsync(request, ct),
            "AMAZON.StopIntent" or "AMAZON.CancelIntent" or "AMAZON.NavigateHomeIntent"
                                  => Task.FromResult(SkillResponse.Tell(Goodbye)),
            _                     => Task.FromResult(SkillResponse.Ask(Welcome, Reprompt)) // Help, Fallback, unknown
        };

    // ── NextDepartureIntent ──────────────────────────────────────────────────

    private async Task<SkillResponse> NextDepartureAsync(SkillRequest request, CancellationToken ct)
    {
        var slotValues = request.Request.Intent?.Slots;
        var awaiting   = Attribute(request, AttrAwaiting);

        // "Meine Haltestelle ist …" without a stop → we asked, and the answer ("Krenngasse") lands here
        if (awaiting == AwaitHomeStop)
            return await SaveHomeStopAsync(request, Slot(slotValues, "stop"), ct);

        // Values from the previous turn fill in what the follow-up ("in der Krenngasse") doesn't repeat
        var fromSession = awaiting == AwaitDeparture;
        var line      = SlotResolver.ResolveLine(Slot(slotValues, "line"))
                        ?? (fromSession ? Attribute(request, AttrLine) : null);
        var direction = SlotResolver.ResolveText(Slot(slotValues, "direction"))
                        ?? (fromSession ? Attribute(request, AttrDirection) : null);

        var resolution = await slots.ResolveStopAsync(Slot(slotValues, "stop"), ct);
        var stop       = resolution.Stop;

        if (resolution.IsMissing && request.UserId is { } userId)
            stop = await homeStops.GetAsync(userId, ct);

        if (stop is null && resolution.IsMissing)
        {
            var attributes = new Dictionary<string, string> { [AttrAwaiting] = AwaitDeparture };
            if (line is not null)      attributes[AttrLine]      = line;
            if (direction is not null) attributes[AttrDirection] = direction;
            return SkillResponse.Ask(AskStop, AskStopAgain, attributes);
        }

        if (stop is null)
            return SkillResponse.Tell(UnknownStop(resolution.Spoken!));

        logger.LogInformation("Departures {StopId} ({Stop}) line {Line} direction {Direction}",
            stop.Id, stop.Name, line ?? "-", direction ?? "-");

        return SkillResponse.Tell(await answers.AnswerAsync(stop, line, direction, ct));
    }

    // ── Home stop ────────────────────────────────────────────────────────────

    private Task<SkillResponse> SetHomeStopAsync(SkillRequest request, CancellationToken ct) =>
        SaveHomeStopAsync(request, Slot(request.Request.Intent?.Slots, "stop"), ct);

    private async Task<SkillResponse> SaveHomeStopAsync(SkillRequest request, Slot? stopSlot, CancellationToken ct)
    {
        var resolution = await slots.ResolveStopAsync(stopSlot, ct);
        if (resolution.IsMissing)
            return SkillResponse.Ask(AskHomeStop, AskHomeStop,
                new Dictionary<string, string> { [AttrAwaiting] = AwaitHomeStop });

        if (resolution.Stop is not { } stop)
            return SkillResponse.Tell(UnknownStop(resolution.Spoken!));

        if (request.UserId is not { } userId)
            return SkillResponse.Tell("Ich kann deine Haltestelle leider nicht speichern.");

        await homeStops.SetAsync(userId, stop, ct);
        return SkillResponse.Tell($"Alles klar, ich habe {stop.Name} als deine Haltestelle gespeichert.");
    }

    private async Task<SkillResponse> GetHomeStopAsync(SkillRequest request, CancellationToken ct)
    {
        var stop = request.UserId is { } userId ? await homeStops.GetAsync(userId, ct) : null;
        return SkillResponse.Tell(stop is null
            ? "Du hast noch keine Haltestelle gespeichert. Sag zum Beispiel: Meine Haltestelle ist Krenngasse."
            : $"Deine Haltestelle ist {stop.Name}.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static string UnknownStop(string spoken) => $"Die Haltestelle {spoken} kenne ich nicht.";

    private static Slot? Slot(Dictionary<string, Slot>? slots, string name) =>
        slots is not null && slots.TryGetValue(name, out var slot) ? slot : null;

    private static string? Attribute(SkillRequest request, string name) =>
        request.Session is { New: false, Attributes: { } attributes }
        && attributes.TryGetValue(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
