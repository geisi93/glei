#!/usr/bin/env bash
# Builds glei for Linux as .deb package.
#
#   linux/packaging/build-deb.sh [version] [linux-x64|linux-arm64] [publish-dir]
#
# version:     SemVer like the release tag (0.2.0, 0.2.0-beta.1)
# publish-dir: an existing self-contained `dotnet publish` output to package (the release workflow passes
#              the one it also packs as AppImage, so both contain the same build); without it, it publishes itself.
#
# Result: linux/packaging/out/glei_<version>_<arch>.deb
#   Install:   sudo apt install ./linux/packaging/out/glei_<version>_amd64.deb
#   Uninstall: sudo apt remove glei
set -euo pipefail
umask 022

VERSION="${1:-0.1.0}"
RID="${2:-linux-x64}"
PUBLISH_DIR="${3:-}"
# Debian sorts "-beta.1" after the final version, "~beta.1" before it
DEB_VERSION="${VERSION//-/\~}"
case "$RID" in
  linux-x64)   ARCH=amd64 ;;
  linux-arm64) ARCH=arm64 ;;
  *) echo "Unknown runtime '$RID' (linux-x64 or linux-arm64)" >&2; exit 1 ;;
esac

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
OUT="$HERE/out"
PACKAGE="glei_${DEB_VERSION}_${ARCH}"
ROOT="$OUT/$PACKAGE"

rm -rf "$ROOT"
mkdir -p "$ROOT/DEBIAN" "$ROOT/opt/glei" "$ROOT/usr/bin" "$ROOT/usr/share/applications" \
         "$ROOT/usr/share/icons/hicolor/256x256/apps" "$ROOT/etc/xdg/autostart"

# Self-contained: no .NET runtime needed on the target machine
if [ -n "$PUBLISH_DIR" ]; then
  cp -a "$PUBLISH_DIR/." "$ROOT/opt/glei/"
else
  dotnet publish "$REPO/linux/TriasDeparture.Linux" -c Release -r "$RID" --self-contained true \
    -p:Version="$VERSION" -o "$ROOT/opt/glei"
fi
rm -f "$ROOT/opt/glei/"*.pdb
echo deb > "$ROOT/opt/glei/install-kind"   # tells glei it is package-managed (InstallKind.Package)

ln -s /opt/glei/glei "$ROOT/usr/bin/glei"
install -m 644 "$HERE/glei.desktop" "$ROOT/usr/share/applications/glei.desktop"
install -m 644 "$HERE/glei-autostart.desktop" "$ROOT/etc/xdg/autostart/glei.desktop"
install -m 644 "$REPO/linux/TriasDeparture.Linux/Assets/glei.png" "$ROOT/usr/share/icons/hicolor/256x256/apps/glei.png"

cat > "$ROOT/DEBIAN/control" <<CONTROL
Package: glei
Version: $DEB_VERSION
Architecture: $ARCH
Maintainer: Matthias Geisriegler <geisi93@users.noreply.github.com>
Section: utils
Priority: optional
Homepage: https://github.com/geisi93/glei
Installed-Size: $(du -sk "$ROOT" | cut -f1)
Depends: libc6, libstdc++6, libgcc-s1, zlib1g, libssl3t64 | libssl3,
 libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu72 | libicu70,
 libfontconfig1, libfreetype6, libglib2.0-0t64 | libglib2.0-0,
 libx11-6, libice6, libsm6, libxi6, libxrandr2, libxcursor1, libxext6, libxfixes3
Recommends: libnotify-bin, libgl1
Suggests: gnome-shell-extension-appindicator
Description: Live-Abfahrten für Bim und Bus in Graz im Panel
 glei zeigt im Panel (Tray) die Minuten bis zur nächsten Abfahrt deiner
 Lieblingslinie an einer Haltestelle in Graz. Ein Klick öffnet die nächsten
 Abfahrten und eine Haltestellensuche. Daten: Verbund Linie (TRIAS).
CONTROL

# The system-wide autostart entry is configuration: an admin's edits survive upgrades
# (and `apt remove` keeps it, `apt purge` removes it – TryExec makes a leftover one do nothing)
echo /etc/xdg/autostart/glei.desktop > "$ROOT/DEBIAN/conffiles"

# Refresh the desktop's menu and icon caches; never fail the (un)install over it
for script in postinst postrm; do
  cat > "$ROOT/DEBIAN/$script" <<'SCRIPT'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q /usr/share/applications || true; fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true; fi
exit 0
SCRIPT
  chmod 755 "$ROOT/DEBIAN/$script"
done

# Plain files 644, programs 755 (publish leaves odd modes behind)
find "$ROOT" -type d -exec chmod 755 {} +
find "$ROOT/opt/glei" -type f -exec chmod 644 {} +
chmod 755 "$ROOT/opt/glei/glei" "$ROOT/opt/glei/createdump" "$ROOT/DEBIAN/postinst" "$ROOT/DEBIAN/postrm"
chmod 644 "$ROOT/DEBIAN/conffiles"
# xz instead of the host default (zstd on Ubuntu): readable by every dpkg, incl. Debian 11 / Ubuntu 20.04
dpkg-deb -Zxz --root-owner-group --build "$ROOT" "$OUT/$PACKAGE.deb"
rm -rf "$ROOT"
echo "Built $OUT/$PACKAGE.deb"
