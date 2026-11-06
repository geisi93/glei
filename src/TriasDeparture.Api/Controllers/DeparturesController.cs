using Microsoft.AspNetCore.Mvc;
using TriasDeparture.Application.Services;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Api.Controllers;

/// <summary>Departure endpoints for a specific stop.</summary>
[ApiController]
[Route("api/departures")]
[Produces("application/json")]
public sealed class DeparturesController : ControllerBase
{
    private readonly DepartureService _service;

    public DeparturesController(DepartureService service) => _service = service;

    /// <summary>Returns all upcoming departures for the given stop.</summary>
    /// <param name="stopId">Stop ID (StopPointRef), e.g. de:643:1234</param>
    [HttpGet("{stopId}")]
    [ProducesResponseType(typeof(IReadOnlyList<DepartureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDepartures(string stopId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stopId))
            return BadRequest("stopId is required.");

        var departures = await _service.GetDeparturesAsync(stopId, ct);
        return Ok(departures);
    }

    /// <summary>Returns upcoming departures filtered by line number.</summary>
    /// <param name="stopId">Stop ID (StopPointRef)</param>
    /// <param name="line">Line number, e.g. 3</param>
    [HttpGet("{stopId}/line/{line}")]
    [ProducesResponseType(typeof(IReadOnlyList<DepartureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDeparturesByLine(string stopId, string line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stopId) || string.IsNullOrWhiteSpace(line))
            return BadRequest("stopId and line are required.");

        var departures = await _service.GetDeparturesByLineAsync(stopId, line, ct);
        return Ok(departures);
    }
}
