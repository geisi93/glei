using TriasDeparture.Domain.Models;

namespace TriasDeparture.Application.Interfaces;

/// <summary>Contract for communicating with the TRIAS backend.</summary>
public interface ITriasClient
{
    Task<IReadOnlyList<StopDto>> SearchStopsAsync(string query, CancellationToken ct = default);

    /// <param name="includeOnwardStops">
    /// Also fetch the stops each trip calls at afterwards (<see cref="DepartureDto.OnwardStops"/>).
    /// Makes the TRIAS response considerably larger – only request it when needed.
    /// </param>
    Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(
        string stopId, bool includeOnwardStops = false, CancellationToken ct = default);
}
