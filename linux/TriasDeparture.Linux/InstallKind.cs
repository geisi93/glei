namespace TriasDeparture.Linux;

/// <summary>How this glei got onto the machine – decides who owns autostart, app-menu entry and updates.</summary>
public enum InstallKind
{
    /// <summary>Build folder (dotnet run / publish): registers nothing except a hidden entry for the dock icon.</summary>
    Development,

    /// <summary>Velopack AppImage: registers itself per user and updates itself from GitHub Releases.</summary>
    AppImage,

    /// <summary>
    /// .deb package: the package ships the app-menu entry, the icon and a system-wide autostart entry
    /// (/etc/xdg/autostart); glei only writes a per-user override to switch autostart off. Updates come as new packages.
    /// </summary>
    Package
}

public static class InstallKinds
{
    /// <summary>Marker file the .deb puts next to the program (linux/packaging/build-deb.sh).</summary>
    public const string MarkerFile = "install-kind";

    public static InstallKind Detect(bool velopackInstalled, string? baseDirectory = null)
    {
        if (velopackInstalled)
            return InstallKind.AppImage;
        try
        {
            var marker = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, MarkerFile);
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == "deb")
                return InstallKind.Package;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return InstallKind.Development;
    }
}
