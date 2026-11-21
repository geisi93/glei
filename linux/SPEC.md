# glei Tray (Linux) – Spezifikation

Stand: 2026-11-18 · Status: Entwurf

Die Linux-Variante der [Windows-Tray-App](../windows/SPEC.md). Funktionen, Verhalten und Texte sind **gleich**. Dieses Dokument beschreibt nur, was unter Linux anders ist. Alles, was hier nicht steht, gilt wie in der Windows-Spezifikation.

- Das **Tray-Icon** zeigt die Minuten bis zur nächsten Abfahrt des Favoriten.
- Der **Tooltip** zeigt die nächsten 3 Abfahrten.
- Ein **Klick** öffnet ein Popup mit den Abfahrten des Favoriten und einer **Suche nach anderen Haltestellen**.

## 1. Rahmen

| Punkt | Entscheidung |
|---|---|
| Plattform | Linux-Desktops mit StatusNotifierItem-Host (KDE Plasma, GNOME mit AppIndicator-Erweiterung, Xfce, Cinnamon, MATE, Budgie), x64 + ARM64 |
| Technik | .NET 10, [Avalonia](https://avaloniaui.net) 12 mit Fluent-Theme. Tray über Avalonias `TrayIcon` (StatusNotifierItem über D-Bus) |
| Warum Avalonia | WPF gibt es nur unter Windows. Avalonia ist XAML/MVVM wie WPF, das ViewModel und die Logik lassen sich fast unverändert übernehmen |
| Anzeige | X11 und Wayland (über XWayland) |
| Verteilung | Über **GitHub Releases**, gebaut von GitHub Actions, in zwei Formen: **AppImage** mit **Velopack** (inkl. Auto-Update) und **.deb**-Paket (Installieren/Deinstallieren über apt bzw. App Center, ohne Auto-Update) |
| Signierung | Keine (unter Linux nicht üblich) |
| Installation | AppImage: herunterladen, ausführbar machen, starten, keine Root-Rechte. .deb: `sudo apt install ./glei_….deb`, nach `/opt/glei`, deinstallieren mit `sudo apt remove glei` |
| Ordner | `linux/` |
| Oberfläche | Deutsch |

## 2. Funktionen – Unterschiede zu Windows

### 2.1 Tray-Icon

- Zustände wie unter Windows (Zahl, `h`, `–`, ausgegraut, `!`, App-Icon ohne Favorit), mit Echtzeit-Punkt.
- Das Icon wird in **64 × 64 px** gerendert. Der Tray-Host skaliert es auf die Panel-Größe, eine DPI-Abfrage wie unter Windows entfällt.
- **Schriftfarbe:**
  - **KDE Plasma:** nach dem System-Theme (Avalonia `ActualThemeVariant`): schwarz bei hellem, weiß bei dunklem, mit Reaktion auf Theme-Wechsel. Nur dort folgt das Panel standardmäßig dem Farbschema.
  - **Alle anderen** (GNOME, Cinnamon, MATE, Budgie, Xfce): weiß. Deren Panels sind standardmäßig dunkel, auch bei hellem App-Theme.
  - Umgebungsvariable `GLEI_PANEL=light|dark` überschreibt die Erkennung.
  - Die Ziffern haben eine dünne Kontur in der Gegenfarbe, damit sie auch bei falsch erkanntem Panel lesbar bleiben.
- **Tooltip:** Avalonia setzt den Text als `Title` des StatusNotifierItems. Ob und wie er erscheint, entscheidet der Host (KDE zeigt ihn, GNOMEs AppIndicator-Erweiterung nicht).
- **Linksklick:** öffnet bzw. schließt das Popup (`Activate`). Manche Hosts (u. a. Ubuntus AppIndicator-Erweiterung) öffnen bei Linksklick stattdessen das Menü, deshalb steht **„Öffnen“** als erster Menüeintrag.
- **Menü:** „Öffnen“, „Aktualisieren“, „Beim Anmelden starten“ (✓), „Info“, „Beenden“.

### 2.2 Popup

- Gleicher Aufbau und gleiche Bedienung wie unter Windows (Favorit oben, Suche unten, Gruppierung nach Linie und Richtung, ☆/★, `Esc`, `Strg+F`, Lostippen, ↓ in die Ergebnisse).
- Fenster ohne Rahmen, immer oben, nicht in der Taskleiste, ca. 360 × 480 px. Schließt bei Fokusverlust oder `Esc`.
- **Position:** Linux sagt einer App nicht, wo ihr Tray-Icon sitzt. Deshalb:
  0. **Bildschirm:** der mit einem Panel (Arbeitsbereich kleiner als der Bildschirm), bei mehreren der primäre. Ohne Panel-Info der primäre.
  1. Liegt ein Panel oben (Arbeitsbereich beginnt unter dem Bildschirmrand): **oben rechts** darunter.
  2. Liegt es unten: **unten rechts** darüber.
  3. Ist der Arbeitsbereich unbekannt (z. B. Wayland): unter GNOME oben rechts, sonst unten rechts.
- Symbole aus Unicode (← ☆ ★) statt Segoe Fluent Icons.

### 2.3 Erststart

- Beim ersten Start erscheint eine Desktop-Benachrichtigung über `notify-send`: „glei läuft im Panel – klick aufs Symbol (oder wähl „Öffnen“), um deine Haltestelle festzulegen“, mit dem Hinweis auf die GNOME-Erweiterung. Fehlt `notify-send`, entfällt sie.
- Ist noch kein Favorit gesetzt, öffnet sich das Popup gleich mit. So kommt man auch an die App, wenn das Panel gar keinen Tray hat.
- **Beim Anmelden starten** ist standardmäßig **an**. Umgesetzt über `~/.config/autostart/glei.desktop` ([XDG Autostart](https://specifications.freedesktop.org/autostart-spec/)). Abschaltbar im Menü. Gespeichert im selben Feld wie unter Windows (`startWithWindows`), damit das Einstellungsformat gleich bleibt.
- Zusätzlich legt die App einen **Eintrag im App-Menü** an (`~/.local/share/applications/glei.desktop` + Icon unter `~/.local/share/icons/hicolor/256x256/apps/glei.png`). Damit lässt sie sich nach „Beenden“ wieder starten.
- `Exec=` zeigt auf die AppImage-Datei (`$APPIMAGE`), nicht auf den temporären Mount-Ordner. Pfade mit Sonderzeichen werden nach der Desktop-Entry-Spezifikation gequotet (inkl. `%` → `%%`). Beide `.desktop`-Dateien werden bei jedem Start aktualisiert, falls das AppImage verschoben wurde.
- Alle Einträge haben `StartupWMClass=glei` (die WM_CLASS der Fenster), damit das Dock die Fenster der App zuordnet und ihr Icon zeigt.
- Nur das installierte AppImage trägt sich in Autostart und App-Menü ein, der Menüpunkt „Beim Anmelden starten“ ist in Entwicklungs-Builds ausgegraut. Ein Entwicklungs-Build legt nur einen **versteckten** Eintrag `glei-dev.desktop` (`NoDisplay=true`, kein Autostart) und das Icon an. Neuere GNOME-Versionen nehmen das Dock-Icon nur noch aus einem passenden `.desktop`-Eintrag, nicht mehr vom Fenster. Ist ein installiertes glei vorhanden, entfällt der Entwicklungs-Eintrag.

### 2.4 Installationsarten

Die App erkennt beim Start, wie sie installiert ist (`InstallKind`), und richtet sich danach:

| | Entwicklung | AppImage (Velopack) | .deb-Paket |
|---|---|---|---|
| Erkennung | sonst | `UpdateManager.IsInstalled` | Datei `install-kind` mit Inhalt `deb` neben dem Programm |
| App-Menü + Icon | nur versteckter `glei-dev.desktop` (fürs Dock-Icon) | legt die App pro Benutzer an (`~/.local/share/…`) | liefert das Paket (`/usr/share/applications`, `/usr/share/icons`) |
| Autostart | nie | `~/.config/autostart/glei.desktop` anlegen/löschen | Paket liefert `/etc/xdg/autostart/glei.desktop`. Ausschalten = pro Benutzer `~/.config/autostart/glei.desktop` mit `Hidden=true`, Einschalten = diese Datei löschen |
| Updates | keine | Velopack aus GitHub Releases | neues Paket installieren |
| Deinstallieren | – | AppImage + Einträge von Hand löschen | `sudo apt remove glei` |

Das .deb (`linux/packaging/build-deb.sh`):

- **Inhalt:** self-contained (kein .NET nötig), installiert nach `/opt/glei`, mit `/usr/bin/glei` als Link. Im Release-Workflow wird dieselbe `dotnet publish`-Ausgabe verpackt wie im AppImage.
- **Abhängigkeiten:** die nativen Bibliotheken von .NET, Skia und Avalonia.X11 (ICU, OpenSSL, fontconfig, freetype, GLib, libX11/ICE/SM/Xi/Xrandr/Xcursor/Xext/Xfixes).
  - Empfohlen: `libnotify-bin` (Willkommens-Hinweis) und `libgl1`.
  - Nur vorgeschlagen: `gnome-shell-extension-appindicator`. Als Empfehlung würde apt sonst auf KDE/Xfce/Cinnamon die ganze GNOME Shell mitinstallieren.
- **Konfiguration:** `/etc/xdg/autostart/glei.desktop` ist als `conffile` markiert, Änderungen eines Admins überleben also Upgrades. `apt remove` lässt die Datei liegen, `apt purge` entfernt sie. `TryExec=/opt/glei/glei` sorgt dafür, dass eine liegengebliebene Datei nichts tut.
- **Version:** aus dem Tag, `-` wird zu `~` (`0.2.0-beta.1` → `0.2.0~beta.1`), damit Vorabversionen vor der finalen sortieren.
- **Kompression:** xz statt des zstd-Standards von Ubuntu, damit auch ältere dpkg-Versionen (Debian 11, Ubuntu 20.04) das Paket lesen können.
- `postinst`/`postrm` aktualisieren nur die Menü- und Icon-Caches.
- **Wechsel vom AppImage:** Als Paket entfernt glei beim Start die Einträge, die ein früheres AppImage pro Benutzer angelegt hat (Autostart, App-Menü, Icon), weil sie die Einträge des Pakets verdecken würden. Entfernt werden nur Dateien, die glei selbst geschrieben hat.
- Benutzerdaten (`~/.config/glei`, `~/.local/state/glei`) bleiben beim Deinstallieren liegen, wie bei Paketen üblich.

## 3. Aktualisierung

Wie unter Windows (60 s, Backoff 1 → 2 → 5 min, `Retry-After`, Nachrücken, sofort neu bei Popup öffnen/Aufwachen/Entsperren/Netz wieder da). Die Erkennung von Sperre und Standby erfolgt über D-Bus statt `SystemEvents`:

| Ereignis | Quelle |
|---|---|
| Standby / Aufwachen | logind `org.freedesktop.login1.Manager.PrepareForSleep(bool)` (System-Bus) |
| Bildschirm gesperrt / entsperrt | `org.gnome.ScreenSaver.ActiveChanged(bool)` und `org.freedesktop.ScreenSaver.ActiveChanged(bool)` (Session-Bus) |
| Netz wieder da | `NetworkChange.NetworkAvailabilityChanged` (.NET, netlink) |

- Alle D-Bus-Signale sind optional. Fehlt eines, wird das nur geloggt.
- **Rückfall:** Liegen zwei Timer-Ticks (alle 5 s) mehr als 30 s auseinander, war der Rechner im Standby. Dann wird sofort neu abgerufen und der Fehlerzähler zurückgesetzt, auch ohne D-Bus-Signal.

## 4. Daten & Einstellungen

Nach der [XDG Base Directory Specification](https://specifications.freedesktop.org/basedir-spec/):

| Was | Wo |
|---|---|
| Einstellungen | `$XDG_CONFIG_HOME/glei/settings.json` (Standard `~/.config/glei/settings.json`), **gleiches Format** wie unter Windows |
| Log | `$XDG_STATE_HOME/glei/logs/` (Standard `~/.local/state/glei/logs/`), Serilog, rollierend, 7 Tage |
| Single-Instance-Lock und -Socket | `$XDG_RUNTIME_DIR/glei.lock`, `glei.sock`. Ohne `XDG_RUNTIME_DIR`: `$XDG_CACHE_HOME/glei/` (Standard `~/.cache/glei/`, Modus 0700), nie das geteilte `/tmp` |

- API-Basis-URL wie unter Windows: `GLEI_API_URL`, sonst `appsettings.json` neben der Programmdatei, sonst die öffentliche URL.
- Kein Login, keine Telemetrie. User-Agent `glei-linux/<Version>`.

## 5. API

Keine Änderungen. Die App nutzt dieselben Endpunkte wie die Windows-App (`/api/stops/search`, `/api/departures/{stopId}?line=&direction=&limit=`).

## 6. Architektur

```
linux/
  TriasDeparture.Linux/             Avalonia-App (net10.0), Programmdatei „glei“
    Program.cs                      Velopack-Hooks, Single Instance, Avalonia-Start
    App.axaml                       Fluent-Theme, Logging, Verdrahtung
    SingleInstance.cs               flock-Lockdatei + Unix-Socket (statt Mutex/EventWaitHandle)
    LinuxPaths.cs                   XDG-Ordner
    Data/DesktopIntegration.cs      Autostart- und App-Menü-.desktop-Dateien
    Tray/                           TrayController, IconRenderer, FavoriteMonitor, SessionEvents (D-Bus), InfoWindow
    Popup/                          PopupWindow (+ Platzierung), PopupViewModel
    Shared/ (verlinkt)              aus windows/TriasDeparture.Windows: AppInfo, UpdateService, GleiApiClient,
                                    Settings, Departures/*, TrayPresenter
  TriasDeparture.Linux.Tests/       xUnit + Shouldly: Linux-Teile + die verlinkten Windows-Tests der gemeinsamen Logik
```

- **Gemeinsamer Code:** Die plattformunabhängigen Dateien der Windows-App werden als **verlinkte Dateien** (`<Compile Include="..\..\windows\…" Link="Shared\…" />`) mitkompiliert und behalten ihre Namespaces `TriasDeparture.Windows.*`. Es gibt also eine Quelle für API-Client, Einstellungen, Abfahrtslogik und Tooltip-Texte. `FavoriteMonitor` und `PopupViewModel` sind Kopien, weil sie WPFs `DispatcherTimer` verwenden. Unter Linux kommt Avalonias Timer zum Einsatz, der Monitor hat zusätzlich die Standby-Erkennung über den Zeitsprung.
- **Modelle:** Projektverweis auf `core/TriasDeparture.Domain`, wie unter Windows.
- **Single Instance:** Der erste Start hält eine exklusive Lockdatei (`flock`, wird vom Kernel beim Prozessende freigegeben) und lauscht auf einem Unix-Socket. Ein zweiter Start verbindet sich und öffnet damit das Popup der laufenden Instanz. Nur ein gehaltener Lock (`EWOULDBLOCK`) gilt als „läuft schon“. Andere Fehler (fremde Datei, schreibgeschützt, voll …) sind kein Beweis. Dann prüft die App, ob eine laufende Instanz auf dem Socket antwortet, und startet sonst ohne Lock, aber weiterhin mit Socket. Lässt sich auch der Socket nicht nutzen (z. B. Pfad zu lang), läuft die App ohne diese Funktion. Der Grund steht jeweils im Log.

## 7. Build & Release

- **GitHub Actions** auf Tag `linux-v*`, z. B. `linux-v0.1.0` ([linux-release.yml](../.github/workflows/linux-release.yml)):
  1. **Packen**, je ein Job pro Architektur auf eigenem Runner (`linux-x64` auf `ubuntu-latest`, `linux-arm64` auf `ubuntu-24.04-arm`): `dotnet test`, `dotnet publish` (self-contained), `vpk download` (Vorversion für Delta-Updates), `vpk pack` → AppImage + Update-Pakete, ein Velopack-Kanal pro Architektur
     Aus derselben `publish`-Ausgabe zusätzlich `linux/packaging/build-deb.sh` → `glei_<Version>_<amd64|arm64>.deb`
  2. **Veröffentlichen**, erst wenn beide Architekturen gepackt sind. Ein Job lädt alles in einen **Entwurf** „glei für Linux …“: x64-Kanal, dann die beiden `.deb`, zuletzt den arm64-Kanal. Erst dieser letzte Upload veröffentlicht. Schlägt vorher etwas fehl, bleibt nur ein unsichtbarer Entwurf (löschen, Tag neu pushen), und kein AppImage aktualisiert sich auf ein halbes Release.
- **Auto-Update:** wie unter Windows. Velopack prüft beim Start (nach 30 s) und dann alle 24 h. Ein Update wird im Hintergrund geladen und ersetzt das AppImage beim Beenden.
- Linux- und Windows-Releases sind getrennt (eigene Tags), die Versionen dürfen auseinanderlaufen.

## 8. Nicht im Umfang (v1)

- Wie unter Windows: mehrere Favoriten, „towards“, Benachrichtigungen zu Abfahrten
- Weitere Paketformate (`.rpm`, Flatpak/Flathub, Snap, AUR) und ein apt-Repository (für Updates des .deb)
- Natives Wayland (Avalonia läuft über XWayland) und XEmbed-Trays ohne StatusNotifierItem
- macOS

## 9. Offene Punkte

- **Tray ohne StatusNotifierItem-Host** (z. B. reines GNOME ohne Erweiterung): kein Symbol. Die App bleibt über das App-Menü (zweiter Start öffnet das Popup) erreichbar. Eine Erkennung mit Hinweis wäre möglich (`org.kde.StatusNotifierWatcher` auf dem Session-Bus prüfen).
- **Popup-Position unter Wayland:** XWayland kennt die Panels nicht immer. Die Ecke wird dann geraten (siehe 2.2). Manche Compositor ignorieren die gewünschte Position auch ganz.
- **Fokus beim Öffnen:** Je nach Fenstermanager verhindert der Focus-Stealing-Schutz, dass das Popup beim Öffnen aus dem Menü den Fokus bekommt. Dann schließt es nicht automatisch bei Klick daneben, sondern erst mit `Esc` oder einem weiteren Klick.
- **Gemeinsames Projekt:** Statt verlinkter Dateien könnte die gemeinsame Logik in ein eigenes Projekt (z. B. `desktop/TriasDeparture.Desktop`) mit neutralem Namespace wandern. Das ändert die Windows-App und sollte zusammen mit ihr getestet werden.
