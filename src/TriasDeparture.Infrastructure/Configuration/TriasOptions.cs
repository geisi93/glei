namespace TriasDeparture.Infrastructure.Configuration;

/// <summary>Strongly-typed settings for the TRIAS endpoint.</summary>
public sealed class TriasOptions
{
    public const string SectionName = "Trias";

    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Sent as siri:RequestorRef (required by the TRIAS schema, not checked by the endpoint).</summary>
    public string RequestorRef { get; init; } = "glei";
}
