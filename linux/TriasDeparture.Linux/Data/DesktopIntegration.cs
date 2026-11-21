using Avalonia.Platform;
using Serilog;

namespace TriasDeparture.Linux.Data;

/// <summary>
/// Freedesktop integration: autostart entry ("Beim Anmelden starten"), app-menu entry and icon for the installed
/// AppImage; for development builds only a hidden entry, so the dock shows the glei icon while debugging.
/// </summary>
/// <param name="executable">What to start: the AppImage, the apphost, or the dotnet host.</param>
/// <param name="argument">The app's dll when started through the dotnet host (<c>dotnet glei.dll</c>), else null.</param>
public sealed class DesktopIntegration(string configHome, string dataHome, string executable, string? argument = null)
{
    public const string FileName = "glei.desktop";
    public const string DevelopmentFileName = "glei-dev.desktop";

    /// <summary>WM_CLASS of glei's windows (Avalonia uses the program name); the dock matches windows to entries by it.</summary>
    public const string WindowClass = "glei";

    public string AutostartFile => Path.Combine(configHome, "autostart", FileName);
    public string MenuFile => Path.Combine(dataHome, "applications", FileName);
    public string DevelopmentFile => Path.Combine(dataHome, "applications", DevelopmentFileName);
    public string IconFile => Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", "glei.png");

    /// <summary>
    /// What to put into Exec=: inside an AppImage the process runs from a temporary mount, so the AppImage file
    /// itself ($APPIMAGE) must be started; <c>dotnet run</c> / <c>dotnet glei.dll</c> runs in the dotnet host.
    /// </summary>
    public static DesktopIntegration? ForCurrentUser()
    {
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage)
            return new DesktopIntegration(LinuxPaths.ConfigHome, LinuxPaths.DataHome, appImage);
        if (Environment.ProcessPath is not { } process)
            return null;
        var dll = Path.GetFileNameWithoutExtension(process) == "dotnet" ? typeof(DesktopIntegration).Assembly.Location : null;
        return new DesktopIntegration(LinuxPaths.ConfigHome, LinuxPaths.DataHome, process, dll is { Length: > 0 } ? dll : null);
    }

    /// <summary>Contents of glei.desktop (Desktop Entry Specification 1.5).</summary>
    public string Entry(bool autostart, bool development = false)
    {
        var exec = argument is null ? QuoteExec(executable) : $"{QuoteExec(executable)} {QuoteExec(argument)}";
        var lines = new List<string>
        {
            "[Desktop Entry]",
            "Type=Application",
            "Version=1.5",
            development ? "Name=glei (Entwicklung)" : "Name=glei",
            "GenericName=Abfahrten",
            "Comment=Live-Abfahrten für Bim und Bus in Graz",
            $"Exec={exec}",
            "Icon=glei",
            "Terminal=false",
            $"StartupWMClass={WindowClass}",
            "Categories=Utility;",
            "Keywords=Bus;Bim;Straßenbahn;Abfahrt;Graz;"
        };
        if (autostart)
            lines.Add("X-GNOME-Autostart-enabled=true");
        if (autostart || development)
            lines.Add("NoDisplay=true");
        return string.Join('\n', lines) + "\n";
    }

    public bool IsAutostartEnabled => File.Exists(AutostartFile);

    public void ApplyAutostart(bool enabled)
    {
        try
        {
            if (enabled) Write(AutostartFile, Entry(autostart: true));
            else File.Delete(AutostartFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Updating autostart failed");
        }
    }

    /// <summary>Per-user override that switches off the package's system-wide autostart entry (XDG Autostart: Hidden=true).</summary>
    public const string AutostartOffOverride = "[Desktop Entry]\nType=Application\nName=glei\nHidden=true\n";

    /// <summary>
    /// .deb install: the package's /etc/xdg/autostart/glei.desktop starts glei; a ~/.config/autostart/glei.desktop
    /// with Hidden=true switches it off for this user.
    /// </summary>
    public void ApplyPackageAutostart(bool enabled)
    {
        try
        {
            RemoveAppImageEntries();
            if (!enabled)
                Write(AutostartFile, AutostartOffOverride);
            else if (File.Exists(AutostartFile) && File.ReadAllText(AutostartFile).Contains("Hidden=true"))
                File.Delete(AutostartFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Updating autostart failed");
        }
    }

    /// <summary>
    /// After switching from the AppImage to the .deb: per-user entries an AppImage glei wrote would shadow the
    /// package's system-wide ones (and point at an AppImage that may be gone). Only files glei wrote are removed.
    /// </summary>
    private void RemoveAppImageEntries()
    {
        foreach (var file in new[] { AutostartFile, MenuFile })
        {
            if (File.Exists(file) && IsOwnFullEntry(File.ReadAllText(file)))
            {
                File.Delete(file);
                Log.Information("Removed {File} left over from the AppImage", file);
            }
        }
        // The package ships the icon; a per-user copy would shadow updates of it
        if (File.Exists(IconFile))
            File.Delete(IconFile);
    }

    /// <summary>A complete entry written by <see cref="Entry"/> (not the Hidden=true override, not something the user made).</summary>
    internal static bool IsOwnFullEntry(string content) =>
        content.Contains($"StartupWMClass={WindowClass}\n") && content.Contains("Comment=Live-Abfahrten für Bim und Bus in Graz\n")
        && !content.Contains("Hidden=true");

    /// <summary>App-menu entry + icon, so glei can be found and started like any other app.</summary>
    public void InstallMenuEntry()
    {
        try
        {
            Write(MenuFile, Entry(autostart: false));
            InstallIcon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Installing the app-menu entry failed");
        }
    }

    /// <summary>
    /// Development build: a hidden entry (not in the app menu, no autostart) + icon. GNOME's dock no longer uses a
    /// window's own icon; without an entry matching <see cref="WindowClass"/> it shows a generic one.
    /// </summary>
    public void InstallDevelopmentEntry()
    {
        try
        {
            // An installed glei already provides the entry for this window class
            if (!File.Exists(MenuFile))
                Write(DevelopmentFile, Entry(autostart: false, development: true));
            InstallIcon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Installing the development entry failed");
        }
    }

    /// <summary>Writes the icon when missing or different (an update may bring a new one).</summary>
    private void InstallIcon()
    {
        using var asset = AssetLoader.Open(new Uri("avares://glei/Assets/glei.png"));
        using var buffer = new MemoryStream();
        asset.CopyTo(buffer);
        var png = buffer.ToArray();

        if (File.Exists(IconFile) && File.ReadAllBytes(IconFile).AsSpan().SequenceEqual(png))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(IconFile)!);
        File.WriteAllBytes(IconFile, png);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Only rewrite when changed (e.g. the AppImage moved), so file watchers of the desktop stay quiet
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Exec= quoting: arguments with reserved characters in double quotes, with \ " ` $ escaped;
    /// % starts a field code (%f, %u …), so a literal % is written %%.
    /// </summary>
    internal static string QuoteExec(string path)
    {
        if (path.All(c => char.IsAsciiLetterOrDigit(c) || "/._-+".Contains(c)))
            return path;
        var escaped = path.Replace(@"\", @"\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        // In a desktop file, a literal backslash has to be escaped once more (string value level)
        return "\"" + escaped.Replace(@"\", @"\\").Replace("%", "%%") + "\"";
    }
}
