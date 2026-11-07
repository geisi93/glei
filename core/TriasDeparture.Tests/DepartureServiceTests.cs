using Shouldly;
using TriasDeparture.Application.Interfaces;
using TriasDeparture.Application.Services;
using TriasDeparture.Domain.Models;

namespace TriasDeparture.Tests;

public sealed class DepartureServiceTests
{
    private static readonly DateTime T = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTriasClient : ITriasClient
    {
        public Dictionary<string, List<DepartureDto>> Departures { get; } = [];

        public Task<IReadOnlyList<StopDto>> SearchStopsAsync(string query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StopDto>>([]);

        public Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(string stopId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DepartureDto>>(Departures.GetValueOrDefault(stopId, []));
    }

    private static DepartureDto Dep(string line, int minutes) =>
        new(line, "Andritz", minutes, T.AddMinutes(minutes), null, "tram");

    private readonly FakeTriasClient _client = new();
    private DepartureService Service => new(_client);

    [Fact]
    public async Task GetDeparturesByLine_FiltersCaseInsensitive()
    {
        _client.Departures["x:1"] = [Dep("64E", 1), Dep("64", 2), Dep("n5", 3)];

        var result = await Service.GetDeparturesByLineAsync("x:1", "N5");

        result.Select(d => d.Line).ShouldBe(["n5"]);
    }
}
