namespace TriasDeparture.Alexa.Verification;

/// <summary>Skill settings (section "Alexa").</summary>
public sealed class AlexaOptions
{
    public const string SectionName = "Alexa";

    /// <summary>Skill ID from the Alexa developer console (amzn1.ask.skill.…); other skills' requests are rejected.</summary>
    public string SkillId { get; init; } = string.Empty;

    /// <summary>
    /// Verify Amazon's request signature and timestamp. Required by Amazon for HTTPS endpoints –
    /// only disable for local testing with curl.
    /// </summary>
    public bool VerifyRequests { get; init; } = true;
}
