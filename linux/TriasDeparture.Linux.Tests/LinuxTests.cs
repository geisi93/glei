using Avalonia;
using Avalonia.Styling;
using Shouldly;
using TriasDeparture.Linux.Data;
using TriasDeparture.Linux.Popup;
using TriasDeparture.Linux.Tray;

namespace TriasDeparture.Linux.Tests;

public sealed class LinuxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "glei-linux-tests-" + Guid.NewGuid().ToString("N"));

    public LinuxTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ── Desktop integration ─────────────────────────────────────────────────

    private DesktopIntegration Desktop(string exe = "/home/me/Apps/glei.AppImage") =>
        new(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), exe);

    [Fact]
    public void Autostart_WritesAndRemovesTheDesktopFile()
    {
        var desktop = Desktop();

        desktop.ApplyAutostart(true);
        desktop.IsAutostartEnabled.ShouldBeTrue();
        File.ReadAllText(desktop.AutostartFile).ShouldContain("Exec=/home/me/Apps/glei.AppImage\n");
        desktop.AutostartFile.ShouldEndWith(Path.Combine("config", "autostart", "glei.desktop"));

        desktop.ApplyAutostart(false);
        desktop.IsAutostartEnabled.ShouldBeFalse();
        desktop.ApplyAutostart(false); // already gone: no error
    }

    [Fact]
    public void Entry_IsAValidDesktopEntry_HiddenInTheMenuOnlyForAutostart()
    {
        var desktop = Desktop();

        var menu = desktop.Entry(autostart: false);
        menu.ShouldStartWith("[Desktop Entry]\n");
        menu.ShouldContain("Type=Application\n");
        menu.ShouldContain("Icon=glei\n");
        menu.ShouldNotContain("NoDisplay");

        desktop.Entry(autostart: true).ShouldContain("NoDisplay=true\n");
    }

    [Fact]
    public void Entry_MatchesGleisWindows_SoTheDockShowsTheIcon()
    {
        Desktop().Entry(autostart: false).ShouldContain("StartupWMClass=glei\n");
        Desktop().Entry(autostart: true).ShouldContain("StartupWMClass=glei\n");
    }

    [Fact]
    public void DevelopmentEntry_IsHidden_AndStartsTheDll()
    {
        var desktop = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"),
            "/usr/lib/dotnet/dotnet", "/src/glei/bin/Debug/net10.0/glei.dll");

        var entry = desktop.Entry(autostart: false, development: true);

        entry.ShouldContain("Name=glei (Entwicklung)\n");
        entry.ShouldContain("NoDisplay=true\n");
        entry.ShouldContain("Exec=/usr/lib/dotnet/dotnet /src/glei/bin/Debug/net10.0/glei.dll\n");
        entry.ShouldNotContain("Autostart");
        desktop.DevelopmentFile.ShouldEndWith(Path.Combine("applications", "glei-dev.desktop"));
    }

    [Theory]
    [InlineData("/opt/glei/glei", "/opt/glei/glei")]
    [InlineData("/home/me/My Apps/glei.AppImage", "\"/home/me/My Apps/glei.AppImage\"")]
    [InlineData("/home/me/$x/glei", "\"/home/me/\\\\$x/glei\"")]
    public void QuoteExec_QuotesReservedCharacters(string path, string expected) =>
        DesktopIntegration.QuoteExec(path).ShouldBe(expected);

    // ── Tray icon color ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("ubuntu:GNOME", "Light", null, false)]  // GNOME's top bar is always dark
    [InlineData("GNOME", "Dark", null, false)]
    [InlineData("X-Cinnamon", "Light", null, false)]    // Mint: light apps, dark panel
    [InlineData("XFCE", "Light", null, false)]
    [InlineData("KDE", "Light", null, true)]            // Plasma's panel follows the color scheme
    [InlineData("KDE", "Dark", null, false)]
    [InlineData(null, "Light", null, false)]
    [InlineData("GNOME", "Dark", "light", true)]        // $GLEI_PANEL wins
    [InlineData("KDE", "Light", " Dark ", false)]
    [InlineData("KDE", "Light", "bogus", true)]
    public void PanelColor_FollowsTheDesktop(string? desktop, string theme, string? overrideValue, bool light) =>
        IconRenderer.IsPanelLight(desktop, theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark, overrideValue).ShouldBe(light);

    // ── Popup placement ─────────────────────────────────────────────────────

    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);
    private static readonly PixelSize Popup = new(360, 480);

    [Fact]
    public void Placement_TopPanel_TopRightBelowIt()
    {
        var pos = PopupPlacement.Place(Screen, new PixelRect(0, 32, 1920, 1048), Popup, gnome: false);
        pos.ShouldBe(new PixelPoint(1920 - 360 - 12, 32 + 12));
    }

    [Fact]
    public void Placement_BottomPanel_BottomRightAboveIt()
    {
        var pos = PopupPlacement.Place(Screen, new PixelRect(0, 0, 1920, 1036), Popup, gnome: false);
        pos.ShouldBe(new PixelPoint(1920 - 360 - 12, 1036 - 480 - 12));
    }

    [Theory]
    [InlineData(true, 12)]           // GNOME: tray is in the top bar
    [InlineData(false, 1080 - 480 - 12)]
    public void Placement_UnknownPanels_GuessFromDesktop(bool gnome, int y) =>
        PopupPlacement.Place(Screen, Screen, Popup, gnome).Y.ShouldBe(y);

    [Fact]
    public void Placement_ScalesTheGap()
    {
        var pos = PopupPlacement.Place(Screen, Screen, Popup, gnome: true, scaling: 2);
        pos.ShouldBe(new PixelPoint(1920 - 360 - 24, 24));
    }

    // ── Single instance ─────────────────────────────────────────────────────

    [Fact]
    public async Task SingleInstance_SecondStartSignalsTheFirst()
    {
        using var first = SingleInstance.Acquire("glei-test", _dir);
        first.IsFirst.ShouldBeTrue();

        var signaled = new TaskCompletionSource();
        first.Listen(() => signaled.TrySetResult());

        using (var second = SingleInstance.Acquire("glei-test", _dir))
        {
            second.IsFirst.ShouldBeFalse();
            second.SignalFirst();
        }

        await signaled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SingleInstance_LockIsReleasedOnDispose()
    {
        SingleInstance.Acquire("glei-test", _dir).Dispose();
        using var again = SingleInstance.Acquire("glei-test", _dir);
        again.IsFirst.ShouldBeTrue();
    }
}

public sealed class LinuxRobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "glei-linux-tests-" + Guid.NewGuid().ToString("N"));

    public LinuxRobustnessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.Delete(_dir, recursive: true);
    }

    // ── Single instance ─────────────────────────────────────────────────────

    [Fact]
    public void SingleInstance_UnusableLockFile_RunsWithoutInsteadOfCrashing()
    {
        // Stands in for another user's /tmp file: exists, but we may not open it for writing
        var lockPath = Path.Combine(_dir, "glei-test.lock");
        File.WriteAllText(lockPath, "");
        File.SetUnixFileMode(lockPath, UnixFileMode.UserRead);
        if (Environment.UserName == "root") return; // root may open anything

        using var instance = SingleInstance.Acquire("glei-test", _dir);

        instance.IsFirst.ShouldBeTrue();
        instance.Problem.ShouldNotBeNull();
        instance.Listen(() => { }).ShouldNotBeNull(); // no socket, no exception
    }

    [Fact]
    public void SingleInstance_SocketPathTooLong_RunsWithoutSignal()
    {
        // sun_path holds 108 bytes
        var deep = Path.Combine(_dir, new string('x', 120));
        using var instance = SingleInstance.Acquire("glei-test", deep);

        instance.IsFirst.ShouldBeTrue();
        instance.Listen(() => { }).ShouldNotBeNull();
        Should.NotThrow(() => SingleInstance.Acquire("glei-test2", deep).SignalFirst());
    }

    [Fact]
    public void SingleInstance_OtherIoError_IsNotMistakenForARunningInstance()
    {
        // A file where the directory should be: IOException, but not "locked"
        var notADir = Path.Combine(_dir, "file");
        File.WriteAllText(notADir, "");

        using var instance = SingleInstance.Acquire("glei-test", notADir);

        instance.IsFirst.ShouldBeTrue();
        instance.Problem.ShouldNotBeNull();
    }

    [Fact]
    public void SingleInstance_WithoutLock_ALaterStartStillFindsTheRunningOne()
    {
        var lockPath = Path.Combine(_dir, "glei-test.lock");
        File.WriteAllText(lockPath, "");
        File.SetUnixFileMode(lockPath, UnixFileMode.UserRead);
        if (Environment.UserName == "root") return;

        using var first = SingleInstance.Acquire("glei-test", _dir);
        first.IsFirst.ShouldBeTrue();
        first.Listen(() => { });

        using var second = SingleInstance.Acquire("glei-test", _dir);
        second.IsFirst.ShouldBeFalse();
    }

    [Fact]
    public void SingleInstance_CreatesItsDirectoryPrivate()
    {
        var dir = Path.Combine(_dir, "rt");
        using var instance = SingleInstance.Acquire("glei-test", dir);
        File.GetUnixFileMode(dir).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void SingleInstance_MissingDirectory_IsCreated()
    {
        using var instance = SingleInstance.Acquire("glei-test", Path.Combine(_dir, "new"));
        instance.IsFirst.ShouldBeTrue();
        instance.Problem.ShouldBeNull();
    }

    // ── Exec= ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/home/me/100%done/glei.AppImage", "\"/home/me/100%%done/glei.AppImage\"")]
    [InlineData("/home/me/Apps%20/glei", "\"/home/me/Apps%%20/glei\"")]
    public void QuoteExec_EscapesPercent(string path, string expected) =>
        DesktopIntegration.QuoteExec(path).ShouldBe(expected);

    // ── Monitor choice ──────────────────────────────────────────────────────

    private sealed record Monitor(string Name, PixelRect Bounds, PixelRect WorkingArea, bool Primary);

    private static Monitor? Choose(params Monitor[] screens) =>
        PopupPlacement.ChooseScreen(screens, s => s.Bounds, s => s.WorkingArea, s => s.Primary);

    private static readonly PixelRect Left  = new(0, 0, 1920, 1080);
    private static readonly PixelRect Right = new(1920, 0, 2560, 1440);

    [Fact]
    public void ChooseScreen_PanelOnSecondaryMonitor_GoesThere()
    {
        var chosen = Choose(
            new Monitor("primary", Left, Left, true),
            new Monitor("panel", Right, new PixelRect(1920, 0, 2560, 1392), false));
        chosen!.Name.ShouldBe("panel");
    }

    [Fact]
    public void ChooseScreen_PanelsEverywhere_PrefersPrimary()
    {
        var chosen = Choose(
            new Monitor("secondary", Left, new PixelRect(0, 32, 1920, 1048), false),
            new Monitor("primary", Right, new PixelRect(1920, 32, 2560, 1408), true));
        chosen!.Name.ShouldBe("primary");
    }

    [Fact]
    public void ChooseScreen_NoPanelInfo_Primary()
    {
        var chosen = Choose(new Monitor("secondary", Left, Left, false), new Monitor("primary", Right, Right, true));
        chosen!.Name.ShouldBe("primary");
    }

    [Fact]
    public void ChooseScreen_NoScreens_Null() => Choose().ShouldBeNull();
}

public sealed class PackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "glei-linux-tests-" + Guid.NewGuid().ToString("N"));

    public PackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void InstallKind_Velopack_IsAppImage() =>
        InstallKinds.Detect(velopackInstalled: true, _dir).ShouldBe(InstallKind.AppImage);

    [Fact]
    public void InstallKind_DebMarker_IsPackage()
    {
        File.WriteAllText(Path.Combine(_dir, InstallKinds.MarkerFile), "deb\n");
        InstallKinds.Detect(velopackInstalled: false, _dir).ShouldBe(InstallKind.Package);
    }

    [Fact]
    public void InstallKind_NoMarker_IsDevelopment() =>
        InstallKinds.Detect(velopackInstalled: false, _dir).ShouldBe(InstallKind.Development);

    [Fact]
    public void PackageAutostart_OffWritesHiddenOverride_OnRemovesIt()
    {
        var desktop = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/opt/glei/glei");

        desktop.ApplyPackageAutostart(false);
        File.ReadAllText(desktop.AutostartFile).ShouldContain("Hidden=true");

        desktop.ApplyPackageAutostart(true);
        File.Exists(desktop.AutostartFile).ShouldBeFalse();
    }

    [Fact]
    public void PackageAutostart_On_KeepsAnEntryTheUserMadeHimself()
    {
        var desktop = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/opt/glei/glei");
        Directory.CreateDirectory(Path.GetDirectoryName(desktop.AutostartFile)!);
        const string own = "[Desktop Entry]\nType=Application\nName=glei\nExec=glei --my-flag\n";
        File.WriteAllText(desktop.AutostartFile, own);

        desktop.ApplyPackageAutostart(true);

        File.ReadAllText(desktop.AutostartFile).ShouldBe(own);
    }

    [Fact]
    public void Package_RemovesEntriesLeftOverFromTheAppImage()
    {
        var appImage = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/home/me/glei.AppImage");
        appImage.ApplyAutostart(true);
        Directory.CreateDirectory(Path.GetDirectoryName(appImage.MenuFile)!);
        File.WriteAllText(appImage.MenuFile, appImage.Entry(autostart: false));
        Directory.CreateDirectory(Path.GetDirectoryName(appImage.IconFile)!);
        File.WriteAllText(appImage.IconFile, "old icon");

        var package = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/opt/glei/glei");
        package.ApplyPackageAutostart(true);

        File.Exists(package.AutostartFile).ShouldBeFalse(); // the system-wide entry applies now
        File.Exists(package.MenuFile).ShouldBeFalse();
        File.Exists(package.IconFile).ShouldBeFalse();
    }

    [Fact]
    public void Package_AutostartOff_ReplacesTheAppImageEntryWithTheOverride()
    {
        var appImage = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/home/me/glei.AppImage");
        appImage.ApplyAutostart(true);

        var package = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/opt/glei/glei");
        package.ApplyPackageAutostart(false);

        File.ReadAllText(package.AutostartFile).ShouldBe(DesktopIntegration.AutostartOffOverride);
    }

    [Fact]
    public void OwnFullEntry_IsRecognized_OverrideAndForeignEntriesAreNot()
    {
        var desktop = new DesktopIntegration(Path.Combine(_dir, "config"), Path.Combine(_dir, "data"), "/x/glei.AppImage");
        DesktopIntegration.IsOwnFullEntry(desktop.Entry(autostart: true)).ShouldBeTrue();
        DesktopIntegration.IsOwnFullEntry(desktop.Entry(autostart: false)).ShouldBeTrue();
        DesktopIntegration.IsOwnFullEntry(DesktopIntegration.AutostartOffOverride).ShouldBeFalse();
        DesktopIntegration.IsOwnFullEntry("[Desktop Entry]\nName=glei\nExec=glei\n").ShouldBeFalse();
    }
}
