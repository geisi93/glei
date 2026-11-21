using System.Diagnostics;
using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Serilog;
using TriasDeparture.Linux.Data;
using TriasDeparture.Linux.Popup;
using TriasDeparture.Windows.Data;
using TriasDeparture.Windows.Tray;

namespace TriasDeparture.Linux.Tray;

/// <summary>Wires the tray icon, its menu, the popup, the settings and the system events together.</summary>
public sealed class TrayController : IDisposable
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly Application _app;
    private readonly IGleiApi _api;
    private readonly SettingsStore _store;
    private readonly InstallKind _install;
    private readonly AppSettings _settings;
    private readonly FavoriteMonitor _monitor;
    private readonly SessionEvents _session = new();
    private readonly string? _desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");

    private TrayIcon? _icon;
    private PopupWindow? _popup;
    private PopupViewModel? _viewModel;
    private TrayView? _shown;
    private bool _lightPanel;

    public TrayController(Application app, IGleiApi api, SettingsStore store, InstallKind install)
    {
        _app         = app;
        _api         = api;
        _store       = store;
        _install     = install;
        _settings    = store.Load();
        _monitor     = new FavoriteMonitor(api);
    }

    public void Start()
    {
        _lightPanel = IconRenderer.IsPanelLight(_desktop, _app.ActualThemeVariant, Environment.GetEnvironmentVariable("GLEI_PANEL"));
        _viewModel  = new PopupViewModel(_api, _monitor, _settings, () => _store.Save(_settings), SetFavorite);
        _popup      = new PopupWindow(_viewModel);

        _icon = new TrayIcon
        {
            ToolTipText = "glei",
            Icon        = IconRenderer.AppIcon(),
            Menu        = BuildMenu()
        };
        _icon.Clicked += (_, _) => TogglePopup();
        TrayIcon.SetIcons(_app, [_icon]);

        _monitor.Changed += Render;
        _monitor.SetFavorite(_settings.Favorite);
        _monitor.Start();
        Render();

        _app.ActualThemeVariantChanged += OnThemeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _session.SleepChanged += OnSleepChanged;
        _session.LockChanged  += OnLockChanged;
        _ = _session.StartAsync();

        // AppImage: keep autostart and app-menu entry pointing at the current AppImage.
        // .deb: the package owns menu entry and autostart; only the per-user "off" override is ours.
        // Development builds never register autostart or a menu entry, only a hidden entry for the dock icon.
        if (DesktopIntegration.ForCurrentUser() is { } desktop)
        {
            switch (_install)
            {
                case InstallKind.AppImage:
                    desktop.ApplyAutostart(_settings.StartWithWindows);
                    desktop.InstallMenuEntry();
                    break;
                case InstallKind.Package:
                    desktop.ApplyPackageAutostart(_settings.StartWithWindows);
                    break;
                default:
                    desktop.InstallDevelopmentEntry();
                    break;
            }
        }

        if (!_settings.WelcomeShown)
        {
            Notify("glei läuft im Panel",
                "Klick aufs Symbol (oder wähl „Öffnen“), um deine Haltestelle festzulegen. " +
                "Unter GNOME braucht es dafür die Erweiterung „AppIndicator and KStatusNotifierItem Support“ (bei Ubuntu schon dabei).");
            _settings.WelcomeShown = true;
            _store.Save(_settings);
            // Without a favorite there is nothing to see in the panel yet – and some panels hide new icons
            if (_settings.Favorite is null)
                Dispatcher.UIThread.Post(ShowPopup, DispatcherPriority.Background);
        }
    }

    public void ShowPopup() => _popup?.ShowPopup();

    private void TogglePopup()
    {
        if (_popup is null) return;

        // The click on the icon first deactivates (hides) the popup – don't reopen it right away
        if (_popup.IsVisible) _popup.HidePopup();
        else if (DateTime.UtcNow - _popup.HiddenAtUtc > ReopenGuard) _popup.ShowPopup();
    }

    private void SetFavorite(Favorite favorite)
    {
        Log.Information("Favorite: {Stop} line {Line} {Direction}", favorite.StopId, favorite.Line, favorite.Direction);
        _settings.Favorite = favorite;
        _store.Save(_settings);
        _monitor.SetFavorite(favorite);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void Render()
    {
        if (_icon is null) return;

        var view = TrayPresenter.Present(_settings.Favorite, _monitor.Snapshot, DateTime.UtcNow);
        if (view != _shown)
        {
            _icon.Icon = view.Text is null
                ? IconRenderer.AppIcon()
                : IconRenderer.Render(view.Text, _lightPanel, view.Dimmed, view.RealtimeDot);
            _icon.ToolTipText = view.ToolTip;
            _shown = view;
        }

        if (_popup?.IsVisible == true)
        {
            _viewModel!.RenderFavorite();
            _viewModel.RenderStop();
        }
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private NativeMenu BuildMenu()
    {
        // Many panels (e.g. Ubuntu's AppIndicator extension) open the menu on left click, so "Öffnen" is in here too
        var open = new NativeMenuItem("Öffnen");
        open.Click += (_, _) => ShowPopup();

        var refresh = new NativeMenuItem("Aktualisieren");
        refresh.Click += async (_, _) => await _monitor.RefreshAsync();

        var autostart = new NativeMenuItem("Beim Anmelden starten")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked  = _settings.StartWithWindows,
            IsEnabled  = _install != InstallKind.Development
        };
        autostart.Click += (_, _) =>
        {
            autostart.IsChecked = !autostart.IsChecked;
            _settings.StartWithWindows = autostart.IsChecked;
            _store.Save(_settings);
            if (DesktopIntegration.ForCurrentUser() is not { } desktop) return;
            if (_install == InstallKind.Package) desktop.ApplyPackageAutostart(autostart.IsChecked);
            else desktop.ApplyAutostart(autostart.IsChecked);
        };

        var info = new NativeMenuItem("Info");
        info.Click += (_, _) => Dispatcher.UIThread.Post(InfoWindow.ShowSingle, DispatcherPriority.Background);

        var exit = new NativeMenuItem("Beenden");
        exit.Click += (_, _) => (_app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        return new NativeMenu { Items = { open, refresh, autostart, new NativeMenuItemSeparator(), info, exit } };
    }

    // ── System events (raised on other threads) ──────────────────────────────

    private void OnSleepChanged(bool sleeping) => Dispatcher.UIThread.Post(() =>
    {
        if (sleeping) _monitor.Pause();
        else _monitor.Resume();
    });

    private void OnLockChanged(bool locked) => Dispatcher.UIThread.Post(() =>
    {
        if (locked) _monitor.Pause();
        else _monitor.Resume();
    });

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
            Dispatcher.UIThread.Post(() => _ = _monitor.RefreshAsync());
    }

    /// <summary>System theme switched between light and dark: redraw the number in the matching color.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var light = IconRenderer.IsPanelLight(_desktop, _app.ActualThemeVariant, Environment.GetEnvironmentVariable("GLEI_PANEL"));
        if (light == _lightPanel) return;
        _lightPanel = light;
        _shown = null;
        Render();
    }

    /// <summary>Desktop notification via notify-send (libnotify); silently skipped where it is missing.</summary>
    private static void Notify(string title, string body)
    {
        try
        {
            var start = new ProcessStartInfo("notify-send") { UseShellExecute = false };
            foreach (var arg in new[] { "--app-name=glei", "--icon=glei", title, body })
                start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Information("No notify-send: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        _app.ActualThemeVariantChanged -= OnThemeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _session.Dispose();

        _monitor.Dispose();
        _icon?.Dispose();
    }
}
