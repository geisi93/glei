using System.Collections.Concurrent;
using TriasDeparture.Alexa.Skill;

namespace TriasDeparture.Alexa.HomeStops;

/// <summary>Process-local store for local development and tests (lost on restart).</summary>
public sealed class InMemoryHomeStopStore : IHomeStopStore
{
    private readonly ConcurrentDictionary<string, ResolvedStop> _stops = new();

    public Task<ResolvedStop?> GetAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(_stops.TryGetValue(UserKey.For(userId), out var stop) ? stop : null);

    public Task SetAsync(string userId, ResolvedStop stop, CancellationToken ct = default)
    {
        _stops[UserKey.For(userId)] = stop;
        return Task.CompletedTask;
    }
}
