using System.Text.Json;
using TriasDeparture.Alexa.Models;
using TriasDeparture.Application.Interfaces;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Alexa.Tests;

/// <summary>Builders for departures, slots and Alexa requests.</summary>
internal static class TestData
{
    public static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    public static DepartureDto Dep(
        string line, int minutes, string destination = "Andritz", string? direction = "outward",
        bool cancelled = false, params string[] onward) =>
        new(line, destination, minutes, Now.AddMinutes(minutes), null, "tram", cancelled, direction,
            onward.Length == 0 ? null : onward);

    public static Slot Slot(string name, string? value, string? id = null) =>
        new(name, value, id is null
            ? null
            : new Resolutions([new ResolutionAuthority("amzn1.er-authority", new ResolutionStatus("ER_SUCCESS_MATCH"),
                [new ResolutionValueWrapper(new ResolutionValue(value!, id))])]));

    public static Slot SlotWithMatches(string name, string value, params (string Name, string Id)[] matches) =>
        new(name, value, new Resolutions([new ResolutionAuthority("amzn1.er-authority",
            new ResolutionStatus("ER_SUCCESS_MATCH"),
            matches.Select(m => new ResolutionValueWrapper(new ResolutionValue(m.Name, m.Id))).ToList())]));

    public static SkillRequest Intent(string name, IEnumerable<Slot>? slots = null,
        Dictionary<string, string>? attributes = null, string userId = "amzn1.ask.account.TEST") =>
        Request(new RequestBody("IntentRequest", "req", Now, "de-DE",
            new Intent(name, (slots ?? []).ToDictionary(s => s.Name))), attributes, userId);

    public static SkillRequest Request(RequestBody body, Dictionary<string, string>? attributes = null,
        string userId = "amzn1.ask.account.TEST", string skillId = "amzn1.ask.skill.test")
    {
        var attrs = attributes?.ToDictionary(a => a.Key, a => JsonSerializer.SerializeToElement(a.Value));
        return new SkillRequest("1.0",
            new Session(attributes is null, "session", new AlexaApplication(skillId), new User(userId), attrs),
            new RequestContext(new SystemContext(new AlexaApplication(skillId), new User(userId))),
            body);
    }
}

/// <summary>In-memory TRIAS client; counts calls that request onward stops.</summary>
internal sealed class FakeTriasClient : ITriasClient
{
    public List<StopDto> Stops { get; } = [];
    public Dictionary<string, List<DepartureDto>> Departures { get; } = [];
    public Dictionary<string, List<DepartureDto>> DeparturesWithOnward { get; } = [];
    public int OnwardRequests { get; private set; }
    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<StopDto>> SearchStopsAsync(string query, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<StopDto>>(Stops);
    }

    public Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(
        string stopId, bool includeOnwardStops = false, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        if (includeOnwardStops) OnwardRequests++;

        var source = includeOnwardStops ? DeparturesWithOnward : Departures;
        return Task.FromResult<IReadOnlyList<DepartureDto>>(source.GetValueOrDefault(stopId, []));
    }
}
