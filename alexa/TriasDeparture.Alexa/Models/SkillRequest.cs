using System.Text.Json;

namespace TriasDeparture.Alexa.Models;

// Minimal subset of the Alexa request JSON that this skill needs.
// https://developer.amazon.com/docs/custom-skills/request-and-response-json-reference.html

/// <summary>Request body Alexa POSTs to the skill endpoint.</summary>
public sealed record SkillRequest(
    string Version,
    Session? Session,
    RequestContext? Context,
    RequestBody Request)
{
    /// <summary>Skill ID the request was sent for (context is always present, session only in-session).</summary>
    public string? ApplicationId =>
        Context?.System?.Application?.ApplicationId ?? Session?.Application?.ApplicationId;

    /// <summary>Stable, skill-specific ID of the Amazon account.</summary>
    public string? UserId => Context?.System?.User?.UserId ?? Session?.User?.UserId;
}

public sealed record Session(
    bool New,
    string? SessionId,
    AlexaApplication? Application,
    User? User,
    Dictionary<string, JsonElement>? Attributes);

public sealed record RequestContext(SystemContext? System);

public sealed record SystemContext(AlexaApplication? Application, User? User);

public sealed record AlexaApplication(string? ApplicationId);

public sealed record User(string? UserId);

/// <summary>
/// Union of the request types this skill handles (LaunchRequest, IntentRequest, SessionEndedRequest).
/// Fields that don't apply to a type stay null.
/// </summary>
public sealed record RequestBody(
    string Type,
    string? RequestId,
    DateTimeOffset Timestamp,
    string? Locale,
    Intent? Intent = null,
    string? Reason = null);

public sealed record Intent(string Name, Dictionary<string, Slot>? Slots);

public sealed record Slot(string Name, string? Value, Resolutions? Resolutions);

public sealed record Resolutions(IReadOnlyList<ResolutionAuthority>? ResolutionsPerAuthority);

public sealed record ResolutionAuthority(
    string? Authority,
    ResolutionStatus? Status,
    IReadOnlyList<ResolutionValueWrapper>? Values);

/// <param name="Code">ER_SUCCESS_MATCH, ER_SUCCESS_NO_MATCH, ER_ERROR_TIMEOUT, ER_ERROR_EXCEPTION</param>
public sealed record ResolutionStatus(string? Code);

public sealed record ResolutionValueWrapper(ResolutionValue Value);

/// <param name="Name">Canonical slot value from the interaction model.</param>
/// <param name="Id">ID of the slot value (for STOP: the TRIAS stop ID).</param>
public sealed record ResolutionValue(string Name, string? Id);
