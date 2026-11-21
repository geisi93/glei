using System.Net.Http;
using System.Text.Json;
using Avalonia.Threading;
using Serilog;
using TriasDeparture.Windows.Data;
using TriasDeparture.Windows.Departures;
using TriasDeparture.Windows.Tray;

namespace TriasDeparture.Linux.Tray;

/// <summary>
/// Keeps the favorite's departures fresh: fetch every 60 s (backoff on errors, Retry-After on 429),
/// re-render every 5 s from the stored absolute times. Paused while the session is locked or asleep.
/// Same behaviour as the Windows FavoriteMonitor, on Avalonia's dispatcher.
/// </summary>
public sealed class FavoriteMonitor : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinRefillGap = TimeSpan.FromSeconds(15);

    /// <summary>Ticks further apart than this mean the machine slept (when no sleep signal came over D-Bus).</summary>
    internal static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(30);

    private const int FetchLimit = 10;

    private readonly IGleiApi _api;
    private readonly Func<DateTime> _utcNow;
    private readonly DispatcherTimer _timer;

    private Favorite? _favorite;
    private int _generation;        // discards answers that arrive after the favorite changed
    private DateTime _nextFetchUtc = DateTime.MinValue;
    private DateTime _lastTickUtc = DateTime.MinValue;
    private int _errors;
    private int _lastFetchedCount;
    private bool _paused;
    private bool _fetching;

    public FavoriteSnapshot Snapshot { get; private set; } = FavoriteSnapshot.Empty;

    /// <summary>Raised on every tick and after every fetch – listeners re-render from <see cref="Snapshot"/>.</summary>
    public event Action? Changed;

    public FavoriteMonitor(IGleiApi api, Func<DateTime>? utcNow = null)
    {
        _api    = api;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _timer  = new DispatcherTimer(Tick, DispatcherPriority.Background, (_, _) => OnTick());
        _timer.Stop();
    }

    public void Start() => _timer.Start();

    public void SetFavorite(Favorite? favorite)
    {
        _favorite         = favorite;
        _generation++;
        Snapshot          = FavoriteSnapshot.Empty;
        _errors           = 0;
        _lastFetchedCount = 0;
        _nextFetchUtc     = DateTime.MinValue;
        Changed?.Invoke();
        _ = FetchAsync();
    }

    public void Pause()
    {
        _paused = true;
        Log.Debug("Paused");
    }

    /// <summary>Back from lock/standby or network: fetch right away.</summary>
    public void Resume()
    {
        _paused = false;
        _ = FetchAsync();
    }

    public Task RefreshAsync() => FetchAsync();

    internal void OnTick()
    {
        var now = _utcNow();
        var gap   = _lastTickUtc == DateTime.MinValue ? TimeSpan.Zero : now - _lastTickUtc;
        var slept = gap > SleepGap;
        _lastTickUtc = now;

        if (_paused || _favorite is null)
            return;

        var remaining = DepartureBoard.Next(Snapshot.Departures, now).Count;
        var refill    = RefreshPolicy.NeedsRefill(remaining, _lastFetchedCount)
                        && now - (Snapshot.FetchedAtUtc ?? DateTime.MinValue) > MinRefillGap;

        if (slept)
        {
            // Woken up: the old answer is stale and the network may still be coming back – start fresh
            Log.Debug("Clock jumped {Gap}, refreshing", gap);
            _errors = 0;
        }

        if (now >= _nextFetchUtc || refill || slept)
            _ = FetchAsync();

        Changed?.Invoke();
    }

    private async Task FetchAsync()
    {
        if (_favorite is not { } favorite || _fetching)
            return;

        _fetching = true;
        var generation = _generation;
        try
        {
            var departures = await _api.GetDeparturesAsync(favorite.StopId, favorite.Line, favorite.Direction, FetchLimit);
            if (generation != _generation)
                return;

            var now = _utcNow();
            Snapshot          = new FavoriteSnapshot(departures, now, Offline: false);
            _lastFetchedCount = DepartureBoard.Next(departures, now, FetchLimit).Count;
            _errors           = 0;
            _nextFetchUtc     = now + RefreshPolicy.Interval;
        }
        catch (RateLimitedException ex) when (generation == _generation)
        {
            Log.Warning("Rate limited, waiting {RetryAfter}", ex.RetryAfter);
            Snapshot      = Snapshot with { Offline = true };
            _nextFetchUtc = _utcNow() + ex.RetryAfter;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && generation == _generation)
        {
            _errors++;
            Log.Warning("Fetching departures failed ({Errors}x): {Message}", _errors, ex.Message);
            Snapshot      = Snapshot with { Offline = true };
            _nextFetchUtc = _utcNow() + RefreshPolicy.AfterError(_errors);
        }
        finally
        {
            _fetching = false;
            Changed?.Invoke();
        }
    }

    public void Dispose() => _timer.Stop();
}
