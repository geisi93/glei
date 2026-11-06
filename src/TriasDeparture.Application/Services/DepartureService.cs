using TriasDeparture.Application.Interfaces;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Application.Services;

/// <summary>Orchestrates stop search and departure fetching.</summary>
public sealed class DepartureService
{
    private readonly ITriasClient _client;

    public DepartureService(ITriasClient client) => _client = client;

    // ────────────────────────────────────────────────────────────────
    // Stop search
    // ────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<StopDto>> SearchStopsAsync(string query, CancellationToken ct = default)
        => _client.SearchStopsAsync(query, ct);

    // ────────────────────────────────────────────────────────────────
    // Departures
    // ────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(string stopId, CancellationToken ct = default)
        => _client.GetDeparturesAsync(stopId, ct);

    public async Task<IReadOnlyList<DepartureDto>> GetDeparturesByLineAsync(
        string stopId, string line, CancellationToken ct = default)
    {
        var all = await _client.GetDeparturesAsync(stopId, ct);
        return all
            .Where(d => IsLine(d, line))
            .ToList()
            .AsReadOnly();
    }

    private static bool IsLine(DepartureDto departure, string line) =>
        string.Equals(departure.Line.Trim(), line.Trim(), StringComparison.OrdinalIgnoreCase);
}
