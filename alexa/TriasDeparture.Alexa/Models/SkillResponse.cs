namespace TriasDeparture.Alexa.Models;

/// <summary>Response body returned to Alexa.</summary>
public sealed record SkillResponse(
    ResponseBody Response,
    Dictionary<string, string>? SessionAttributes = null)
{
    public string Version => "1.0";

    /// <summary>Speaks <paramref name="text"/> and ends the session.</summary>
    public static SkillResponse Tell(string text) =>
        new(new ResponseBody(OutputSpeech.Plain(text), null, ShouldEndSession: true));

    /// <summary>Speaks <paramref name="text"/> and keeps listening; <paramref name="reprompt"/> is used if the user stays silent.</summary>
    public static SkillResponse Ask(string text, string reprompt, Dictionary<string, string>? attributes = null) =>
        new(new ResponseBody(OutputSpeech.Plain(text), new Reprompt(OutputSpeech.Plain(reprompt)), ShouldEndSession: false),
            attributes);

    /// <summary>Response to SessionEndedRequest – Alexa ignores any speech here.</summary>
    public static SkillResponse Empty() => new(new ResponseBody(null, null, ShouldEndSession: null));
}

public sealed record ResponseBody(OutputSpeech? OutputSpeech, Reprompt? Reprompt, bool? ShouldEndSession);

public sealed record Reprompt(OutputSpeech OutputSpeech);

public sealed record OutputSpeech(string Type, string Text)
{
    public static OutputSpeech Plain(string text) => new("PlainText", text);
}
