using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TriasDeparture.Domain.Models;
using TriasDeparture.Windows.Data;
using TriasDeparture.Windows.Departures;
using TriasDeparture.Linux.Tray;

namespace TriasDeparture.Linux.Popup;

public sealed record DepartureRow(string Line, string Destination, string Minutes, bool IsRealtime, bool IsCancelled);

public sealed record StopItem(string Id, string Name)
{
    public string DisplayName => Format.StopName(Name);
}

public sealed partial class GroupItem(DepartureGroup group, string minutes, bool isFavorite) : ObservableObject
{
    public string Line { get; } = group.Line;
    public string? Direction { get; } = group.Direction;
    public string Label { get; } = group.Label;
    public string Minutes { get; } = minutes;
    public bool HasRealtime { get; } = group.Rows.Any(r => r.EstimatedTime is not null && !r.Cancelled);

    /// <summary>A favorite needs a direction (outward/inward) for the API filter.</summary>
    public bool CanBeFavorite => Direction is not null;

    [ObservableProperty]
    private bool _isFavorite = isFavorite;
}

/// <summary>Popup: the favorite on top, stop search below.</summary>
public sealed partial class PopupViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StopRefresh = TimeSpan.FromSeconds(30);

    private readonly IGleiApi _api;
    private readonly FavoriteMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Action<Favorite> _setFavorite;
    private readonly DispatcherTimer _stopTimer;

    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<DepartureDto> _stopDepartures = [];

    public PopupViewModel(IGleiApi api, FavoriteMonitor monitor, AppSettings settings, Action saveSettings, Action<Favorite> setFavorite)
    {
        _api          = api;
        _monitor      = monitor;
        _settings     = settings;
        _saveSettings = saveSettings;
        _setFavorite  = setFavorite;
        _stopTimer    = new DispatcherTimer(StopRefresh, DispatcherPriority.Background, async (_, _) => await LoadStopAsync());
        _stopTimer.Stop();

        foreach (var recent in settings.RecentStops)
            RecentStops.Add(new StopItem(recent.StopId, recent.StopName));
        RenderFavorite();
    }

    // ── Favorite ─────────────────────────────────────────────────────────────

    public ObservableCollection<DepartureRow> FavoriteRows { get; } = [];

    [ObservableProperty]
    private string _favoriteTitle = "";

    [ObservableProperty]
    private string _favoriteStatus = "";

    [ObservableProperty]
    private bool _hasFavorite;

    [ObservableProperty]
    private bool _isFavoriteStale;

    /// <summary>Re-render from the monitor's snapshot (called every tick while visible).</summary>
    public void RenderFavorite()
    {
        var favorite = _settings.Favorite;
        HasFavorite = favorite is not null;
        if (favorite is null)
        {
            FavoriteTitle  = "Noch kein Favorit";
            FavoriteStatus = "Such unten eine Haltestelle und tipp bei deiner Linie auf ☆.";
            FavoriteRows.Clear();
            return;
        }

        var now      = DateTime.UtcNow;
        var snapshot = _monitor.Snapshot;
        FavoriteTitle   = $"{Format.StopName(favorite.StopName)} · {Format.LineName(favorite.Line)} → {favorite.DirectionLabel}";
        IsFavoriteStale = snapshot.Offline;

        var rows = DepartureBoard.Rows(snapshot.Departures, now)
            .Select(d => new DepartureRow(d.Line, d.Destination, d.Cancelled ? "fällt aus" : Format.Minutes(d, now),
                d.EstimatedTime is not null && !d.Cancelled, d.Cancelled))
            .ToList();
        Replace(FavoriteRows, rows);

        FavoriteStatus = snapshot switch
        {
            { FetchedAtUtc: null, Offline: true } => "Keine Verbindung – Rechtsklick → Aktualisieren",
            { FetchedAtUtc: null }                => "Lädt …",
            _ when rows.Count == 0                => "Keine Abfahrten in den nächsten Stunden",
            { Offline: true }                     => $"offline · Stand {Format.Clock(snapshot.FetchedAtUtc!.Value)}",
            _                                     => $"Stand {Format.Clock(snapshot.FetchedAtUtc!.Value)}"
        };
    }

    // ── Search ───────────────────────────────────────────────────────────────

    public ObservableCollection<StopItem> StopResults { get; } = [];
    public ObservableCollection<StopItem> RecentStops { get; } = [];
    public ObservableCollection<GroupItem> Groups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecent), nameof(ShowResults))]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecent), nameof(ShowResults), nameof(ShowStop))]
    private StopItem? _selectedStop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private string? _searchStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private string? _stopStatus;

    /// <summary>Status under the search box: the stop's status while one is open, otherwise the search's.</summary>
    public string StatusLine => new[] { StopStatus, SearchStatus }.FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";

    public bool ShowRecent  => SelectedStop is null && string.IsNullOrWhiteSpace(SearchText) && RecentStops.Count > 0;
    public bool ShowResults => SelectedStop is null && !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowStop    => SelectedStop is not null;

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        _ = SearchAsync(value.Trim(), _searchCts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        if (query.Length < 2)
        {
            StopResults.Clear();
            SearchStatus = query.Length == 0 ? null : "Mindestens 2 Zeichen";
            return;
        }

        try
        {
            await Task.Delay(SearchDelay, ct);
            SearchStatus = "Suche …";
            var stops = await _api.SearchStopsAsync(query, ct);
            if (ct.IsCancellationRequested) return;

            Replace(StopResults, stops.Select(s => new StopItem(s.Id, s.Name)).ToList());
            SearchStatus = stops.Count == 0 ? "Keine Haltestelle gefunden" : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or RateLimitedException)
        {
            Log.Warning("Stop search failed: {Message}", ex.Message);
            SearchStatus = "Suche gerade nicht möglich – keine Verbindung";
        }
    }

    [RelayCommand]
    private async Task OpenStop(StopItem stop)
    {
        SelectedStop = stop;
        Groups.Clear();
        _stopDepartures = [];
        StopStatus = "Lädt …";

        _settings.AddRecent(new RecentStop(stop.Id, stop.Name));
        _saveSettings();
        Replace(RecentStops, _settings.RecentStops.Select(r => new StopItem(r.StopId, r.StopName)).ToList());

        await LoadStopAsync();
        _stopTimer.Start();
    }

    [RelayCommand]
    private void Back()
    {
        _stopTimer.Stop();
        SelectedStop = null;
        Groups.Clear();
        _stopDepartures = [];
        OnPropertyChanged(nameof(ShowRecent));
    }

    private async Task LoadStopAsync()
    {
        if (SelectedStop is not { } stop)
            return;

        try
        {
            var departures = await _api.GetDeparturesAsync(stop.Id);
            if (SelectedStop?.Id != stop.Id) return;
            _stopDepartures = departures;
            RenderStop();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or RateLimitedException)
        {
            Log.Warning("Loading stop {Stop} failed: {Message}", stop.Id, ex.Message);
            StopStatus = _stopDepartures.Count == 0 ? "Keine Verbindung" : "offline – Daten nicht aktuell";
        }
    }

    /// <summary>Regroup from the stored times (minutes tick down without fetching).</summary>
    public void RenderStop()
    {
        if (SelectedStop is not { } stop)
            return;

        var now      = DateTime.UtcNow;
        var favorite = _settings.Favorite;
        var groups   = DepartureBoard.Group(_stopDepartures, now)
            .Select(g => new GroupItem(g, Format.MinutesList(g.Rows, now),
                favorite is not null && favorite.StopId == stop.Id && favorite.Line == g.Line && favorite.Direction == g.Direction))
            .ToList();

        // GroupItems are new objects every time – compare by content, so buttons keep focus between ticks
        static string Key(GroupItem g) => $"{g.Line}|{g.Direction}|{g.Label}|{g.Minutes}|{g.IsFavorite}";
        if (!Groups.Select(Key).SequenceEqual(groups.Select(Key)))
            Replace(Groups, groups);
        StopStatus = groups.Count == 0 ? "Keine Abfahrten in den nächsten Stunden" : null;
    }

    [RelayCommand]
    private void MakeFavorite(GroupItem group)
    {
        if (SelectedStop is not { } stop || group.Direction is null)
            return;

        _setFavorite(new Favorite(stop.Id, stop.Name, group.Line, group.Direction, group.Label));
        foreach (var g in Groups)
            g.IsFavorite = ReferenceEquals(g, group);
        RenderFavorite();
    }

    // ── Popup lifecycle ──────────────────────────────────────────────────────

    public void OnShown()
    {
        RenderFavorite();
        if (SelectedStop is not null)
        {
            _ = LoadStopAsync();
            _stopTimer.Start();
        }
    }

    public void OnHidden() => _stopTimer.Stop();

    /// <summary>Esc: leave the stop, then clear the search; false when there is nothing left to undo.</summary>
    public bool StepBack()
    {
        if (SelectedStop is not null) { Back(); return true; }
        if (SearchText.Length > 0)    { SearchText = ""; return true; }
        return false;
    }

    /// <summary>Update the collection only where it differs, so the list doesn't flicker every tick.</summary>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items))
            return;

        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
