using TriasDeparture.Domain.Models;

namespace TriasDeparture.Application.Interfaces;

/// <summary>Contract for communicating with the TRIAS backend.</summary>
public interface ITriasClient
{
    Task<IReadOnlyList<StopDto>> SearchStopsAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(string stopId, CancellationToken ct = default);
}
