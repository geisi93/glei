using Microsoft.AspNetCore.Mvc;
using TriasDeparture.Application.Services;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Api.Controllers;

/// <summary>Stop search endpoint.</summary>
[ApiController]
[Route("api/stops")]
[Produces("application/json")]
public sealed class StopsController : ControllerBase
{
    private readonly DepartureService _service;

    public StopsController(DepartureService service) => _service = service;

    /// <summary>Searches for stops matching the given name fragment.</summary>
    /// <param name="query">Part of the stop name, e.g. "krenngasse"</param>
    /// <returns>List of matching stops with their IDs.</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<StopDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("query parameter is required.");

        var stops = await _service.SearchStopsAsync(query, ct);
        return Ok(stops);
    }
}
