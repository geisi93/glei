using System.Security.Cryptography;
using System.Text;
using TriasDeparture.Alexa.Skill;

namespace TriasDeparture.Alexa.HomeStops;

/// <summary>Remembers each user's home stop ("meine Haltestelle ist …").</summary>
public interface IHomeStopStore
{
    Task<ResolvedStop?> GetAsync(string userId, CancellationToken ct = default);
    Task SetAsync(string userId, ResolvedStop stop, CancellationToken ct = default);
}

internal static class UserKey
{
    /// <summary>The Alexa user ID is long and only meaningful to Amazon – store a SHA-256 hash of it instead.</summary>
    public static string For(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
}
