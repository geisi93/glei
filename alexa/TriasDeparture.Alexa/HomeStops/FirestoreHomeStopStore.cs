using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using TriasDeparture.Alexa.Skill;

namespace TriasDeparture.Alexa.HomeStops;

/// <summary>Firestore settings (section "Firestore").</summary>
public sealed class FirestoreOptions
{
    public const string SectionName = "Firestore";

    /// <summary>Google Cloud project ID. If empty, it is detected from the environment (Cloud Run metadata server).</summary>
    public string? ProjectId { get; init; }
}

/// <summary>
/// Stores home stops in Firestore: collection "users", document ID = SHA-256 of the Alexa user ID,
/// fields stopId, stopName, updatedAt.
/// </summary>
public sealed class FirestoreHomeStopStore(IOptions<FirestoreOptions> options) : IHomeStopStore
{
    private const string Collection = "users";

    // Created on first use so the app starts even before Firestore is reachable/configured
    private readonly Lazy<Task<FirestoreDb>> _db = new(() => CreateAsync(options.Value.ProjectId));

    public async Task<ResolvedStop?> GetAsync(string userId, CancellationToken ct = default)
    {
        var db       = await _db.Value;
        var snapshot = await db.Collection(Collection).Document(UserKey.For(userId)).GetSnapshotAsync(ct);
        if (!snapshot.Exists)
            return null;

        return snapshot.TryGetValue<string>("stopId", out var id) && snapshot.TryGetValue<string>("stopName", out var name)
            ? new ResolvedStop(id, name)
            : null;
    }

    public async Task SetAsync(string userId, ResolvedStop stop, CancellationToken ct = default)
    {
        var db = await _db.Value;
        await db.Collection(Collection).Document(UserKey.For(userId)).SetAsync(new Dictionary<string, object>
        {
            ["stopId"]    = stop.Id,
            ["stopName"]  = stop.Name,
            ["updatedAt"] = Timestamp.GetCurrentTimestamp()
        }, cancellationToken: ct);
    }

    private static Task<FirestoreDb> CreateAsync(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId)
            ? FirestoreDb.CreateAsync()            // project from Application Default Credentials / metadata server
            : FirestoreDb.CreateAsync(projectId);
}
