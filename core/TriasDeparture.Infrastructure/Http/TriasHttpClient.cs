using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TriasDeparture.Application.Interfaces;
using TriasDeparture.Domain.Models;
using TriasDeparture.Infrastructure.Configuration;
using TriasDeparture.Infrastructure.Trias;

namespace TriasDeparture.Infrastructure.Http;

/// <summary>
/// Sends TRIAS requests via HTTP and caches the results in memory.
/// Cache TTLs:
///   stops:{query}        → 24 h
///   departures:{stopId}[:onward]  → 15 s
/// </summary>
public sealed class TriasHttpClient : ITriasClient
{
    private static readonly TimeSpan StopsTtl      = TimeSpan.FromHours(24);
    private static readonly TimeSpan DeparturesTtl = TimeSpan.FromSeconds(15);

    // Busy stops like Jakominiplatz have many lines; a small result set would
    // often miss the requested line entirely when filtering.
    private const int DepartureResults = 50;

    private readonly HttpClient       _http;
    private readonly TriasOptions     _options;
    private readonly IMemoryCache     _cache;
    private readonly TriasXmlBuilder  _builder;
    private readonly TriasXmlParser   _parser;
    private readonly TimeProvider     _time;
    private readonly ILogger<TriasHttpClient> _logger;

    public TriasHttpClient(
        HttpClient                   http,
        IOptions<TriasOptions>       options,
        IMemoryCache                 cache,
        ILogger<TriasHttpClient>     logger,
        TimeProvider                 time)
    {
        _http    = http;
        _options = options.Value;
        _cache   = cache;
        _logger  = logger;
        _time    = time;
        _builder = new TriasXmlBuilder(_options.RequestorRef);
        _parser  = new TriasXmlParser();
    }

    // ────────────────────────────────────────────────────────────────
    // Public API
    // ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StopDto>> SearchStopsAsync(string query, CancellationToken ct = default)
    {
        var cacheKey = $"stops:{query.Trim().ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<StopDto>? cached) && cached is not null)
        {
            _logger.LogDebug("Cache HIT  [{Key}]", cacheKey);
            return cached;
        }

        _logger.LogDebug("Cache MISS [{Key}]", cacheKey);

        var xml      = _builder.BuildLocationInformationRequest(query.Trim());
        var response = await PostAsync(xml, ct);
        var stops    = _parser.ParseStops(response);

        _cache.Set(cacheKey, stops, StopsTtl);
        return stops;
    }

    public async Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(
        string stopId, bool includeOnwardStops = false, CancellationToken ct = default)
    {
        var cacheKey = includeOnwardStops ? $"departures:{stopId}:onward" : $"departures:{stopId}";

        if (!_cache.TryGetValue(cacheKey, out IReadOnlyList<DepartureDto>? departures) || departures is null)
        {
            _logger.LogDebug("Cache MISS [{Key}]", cacheKey);

            var xml      = _builder.BuildStopEventRequest(
                stopId, _time.GetUtcNow().UtcDateTime, DepartureResults, includeOnwardStops);
            var response = await PostAsync(xml, ct);
            departures   = _parser.ParseDepartures(response, _time.GetUtcNow().UtcDateTime);

            _cache.Set(cacheKey, departures, DeparturesTtl);
        }
        else
        {
            _logger.LogDebug("Cache HIT  [{Key}]", cacheKey);
        }

        // Recompute minutes on every read so cached entries never report stale countdowns,
        // and drop departures that have already left.
        var now = _time.GetUtcNow().UtcDateTime;
        return departures
            .Where(d => d.EffectiveTime >= now.AddSeconds(-30))
            .Select(d => d with { Minutes = d.MinutesUntilDeparture(now) })
            .ToList()
            .AsReadOnly();
    }

    // ────────────────────────────────────────────────────────────────
    // HTTP helper
    // ────────────────────────────────────────────────────────────────

    private async Task<string> PostAsync(string requestXml, CancellationToken ct)
    {
        // TRIAS requires exactly "Content-Type: text/xml" (encoding is declared in the XML prolog)
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(requestXml));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = content
        };

        _logger.LogInformation("→ POST {Url}", _options.BaseUrl);
        var sw = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
            sw.Stop();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "TRIAS request failed after {Ms} ms", sw.ElapsedMilliseconds);
            throw;
        }

        using (response)
        {
            _logger.LogInformation("← {Status} in {Ms} ms", (int)response.StatusCode, sw.ElapsedMilliseconds);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
    }
}
