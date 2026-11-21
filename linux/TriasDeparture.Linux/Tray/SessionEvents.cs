using Serilog;
using Tmds.DBus.Protocol;

namespace TriasDeparture.Linux.Tray;

/// <summary>
/// Lock and sleep on Linux via D-Bus – the counterpart to SystemEvents.SessionSwitch/PowerModeChanged on Windows:
/// <list type="bullet">
/// <item>logind <c>PrepareForSleep(bool)</c> on the system bus (true before suspend, false after resume)</item>
/// <item><c>ActiveChanged(bool)</c> of org.gnome.ScreenSaver / org.freedesktop.ScreenSaver on the session bus (lock screen)</item>
/// </list>
/// Everything is optional: without D-Bus, the monitor still notices a wake-up by the jump of the clock.
/// Events are raised on a D-Bus thread.
/// </summary>
public sealed class SessionEvents : IDisposable
{
    private readonly List<IDisposable> _watches = [];

    /// <summary>true = going to sleep / screen locked; false = woken up / unlocked.</summary>
    public event Action<bool>? SleepChanged;
    public event Action<bool>? LockChanged;

    public async Task StartAsync()
    {
        await WatchAsync(() => DBusConnection.System, "org.freedesktop.login1.Manager", "PrepareForSleep", v => SleepChanged?.Invoke(v));
        await WatchAsync(() => DBusConnection.Session, "org.gnome.ScreenSaver", "ActiveChanged", v => LockChanged?.Invoke(v));
        await WatchAsync(() => DBusConnection.Session, "org.freedesktop.ScreenSaver", "ActiveChanged", v => LockChanged?.Invoke(v));
    }

    private async Task WatchAsync(Func<DBusConnection?> connection, string @interface, string signal, Action<bool> handler)
    {
        try
        {
            if (connection() is not { } bus)
                return;

            var watch = await bus.WatchSignalAsync(
                null, null, @interface, signal,
                (message, _) => message.GetBodyReader().ReadBool(),
                (Notification<bool> notification) =>
                {
                    if (notification.HasValue) handler(notification.Value);
                },
                ObserverFlags.None, false, null);
            _watches.Add(watch);
        }
        catch (Exception ex)
        {
            Log.Information("D-Bus signal {Interface}.{Signal} not available: {Message}", @interface, signal, ex.Message);
        }
    }

    public void Dispose()
    {
        foreach (var watch in _watches)
            watch.Dispose();
        _watches.Clear();
    }
}
