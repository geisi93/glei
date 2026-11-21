using Avalonia;
using Velopack;

namespace TriasDeparture.Linux;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles install/update hooks and exits in those cases
        VelopackApp.Build().Run();

        using var instance = SingleInstance.Acquire("glei");
        if (!instance.IsFirst)
        {
            // Second start (e.g. from the app menu while running): just open the popup of the running instance
            instance.SignalFirst();
            return;
        }

        BuildAvaloniaApp()
            .AfterSetup(builder => ((App)builder.Instance!).Instance = instance)
            .StartWithClassicDesktopLifetime(args, lifetime => lifetime.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the Avalonia previewer
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
