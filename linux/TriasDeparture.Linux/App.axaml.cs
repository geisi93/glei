using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;
using TriasDeparture.Linux.Tray;
using TriasDeparture.Windows;
using TriasDeparture.Windows.Data;

namespace TriasDeparture.Linux;

public partial class App : Application
{
    private TrayController? _tray;
    private UpdateService? _updates;

    /// <summary>Set by <see cref="Program"/> before the app starts.</summary>
    public SingleInstance? Instance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(LinuxPaths.LogDirectory, "glei-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
        Log.Information("glei {Version} (Linux) starting, API {Api}, desktop {Desktop}",
            AppInfo.Version, AppConfig.ApiBaseUrl, Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        var http = new HttpClient { BaseAddress = AppConfig.ApiBaseUrl, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"glei-linux/{AppInfo.Version}");

        _updates = new UpdateService();
        var install = InstallKinds.Detect(_updates.IsInstalled);
        Log.Information("Install kind: {Kind}", install);
        _tray = new TrayController(this, new GleiApiClient(http), new SettingsStore(LinuxPaths.SettingsFile), install);
        _tray.Start();

        if (Instance?.Listen(() => Dispatcher.UIThread.Post(() => _tray.ShowPopup())) is { } problem)
            Log.Warning("Running without single instance: {Problem}", problem);
        _updates.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit += (_, _) => OnExit();
    }

    private void OnExit()
    {
        _tray?.Dispose();
        _updates?.Dispose();
        Log.Information("glei exiting");
        Log.CloseAndFlush();
    }
}
