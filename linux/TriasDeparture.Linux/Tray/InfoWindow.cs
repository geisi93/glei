using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TriasDeparture.Windows;

namespace TriasDeparture.Linux.Tray;

/// <summary>"Info" from the tray menu.</summary>
public sealed class InfoWindow : Window
{
    private static InfoWindow? _open;

    public static void ShowSingle()
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        _open = new InfoWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private InfoWindow()
    {
        Title                 = "Info – glei";
        SizeToContent         = SizeToContent.WidthAndHeight;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost               = true;

        var link = new HyperlinkButton { Content = AppInfo.RepositoryUrl, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 20) };
        link.Click += (_, _) => Open(AppInfo.RepositoryUrl);

        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            MinWidth = 320,
            Children =
            {
                new TextBlock { Text = "glei", FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"Version {AppInfo.Version}", Opacity = 0.65, Margin = new Thickness(0, 2, 0, 12) },
                new TextBlock { Text = "Live-Abfahrten für Bim und Bus in Graz." },
                new TextBlock { Text = "Daten: Verbund Linie (TRIAS)", Margin = new Thickness(0, 4, 0, 0) },
                link,
                ok
            }
        };
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Serilog.Log.Warning(ex, "Opening {Url} failed", url);
        }
    }
}
