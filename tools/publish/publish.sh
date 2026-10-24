#!/usr/bin/env bash
# Builds FbStudio for Windows, Linux and macOS, self-contained (no .NET install needed to run it).
# Runs on Linux or macOS (or GitHub Actions): needs the .NET 10 SDK, zip and tar.
#
#   tools/publish/publish.sh                    → the version in Directory.Build.props, every platform
#   tools/publish/publish.sh 0.2.0              → another version
#   TARGETS="win linux" tools/publish/publish.sh → only some platforms (win, linux, mac)
#
# Output in artifacts/:
#   FbStudio-<version>-win-x64.zip          FbStudio.exe (a single file)
#   FbStudio-<version>-linux-x64.tar.gz     FbStudio (a single file, already executable)
#   fbstudio_<version>_amd64.deb            the same, as a Debian/Ubuntu package (needs dpkg-deb): installs in
#                                           /opt/fbstudio, the «fbstudio» command and the menu entry with its icon
#   FbStudio-<version>-osx-arm64.zip        FbStudio.app for Apple Silicon Macs (M1 and later)
#   FbStudio-<version>-osx-x64.zip          FbStudio.app for Intel Macs
#
# The Mac packages must be built on a Mac: Apple Silicon only runs signed code, and only a Mac can sign it. There every
# executable file is signed ad hoc (a local signature, without an Apple account); built elsewhere they come out unsigned.
set -euo pipefail
cd "$(dirname "$0")/../.."

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)}"
# Solo números y puntos (y un sufijo como -beta.1): va a sed, al control del .deb y al Info.plist.
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 1
fi
umask 022 # los archivos de los paquetes, legibles por todos y escribibles solo por el dueño
targets="${TARGETS:-win linux mac}"
out="$PWD/artifacts"
rm -rf "$out"
mkdir -p "$out"

wants() { [[ " $targets " == *" $1 "* ]]; }

publish() { # rid, output folder, extra options
  dotnet publish src/FbStudio.App/FbStudio.App.csproj -c Release -r "$1" --self-contained true \
    -p:Version="$version" -p:DebugType=none -o "$2" "${@:3}"
  # DebugType=none quita los símbolos propios; los nativos de SkiaSharp y HarfBuzz para Windows (104 MB) vienen del
  # paquete NuGet y quedaban al lado del .exe. No hacen falta para usar FbStudio.
  find "$2" -name "*.pdb" -delete
}

# Windows and Linux: a single executable (native libraries extract themselves on first run).
single=(-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
if wants win; then
  publish win-x64 "$out/win-x64" "${single[@]}"
  (cd "$out/win-x64" && zip -qr "$out/FbStudio-$version-win-x64.zip" .)
fi

if wants linux; then
  publish linux-x64 "$out/linux-x64" "${single[@]}"
  chmod +x "$out/linux-x64/FbStudio"
  mv "$out/linux-x64" "$out/FbStudio-$version"
  tar -C "$out" -czf "$out/FbStudio-$version-linux-x64.tar.gz" "FbStudio-$version"
  mv "$out/FbStudio-$version" "$out/linux-x64"

  # The .deb: «sudo apt install ./fbstudio_<version>_amd64.deb» installs it (and its dependencies), «apt remove
  # fbstudio» removes it. The ICU package changes name with each distribution version: any of them will do.
  if command -v dpkg-deb > /dev/null; then
    pkg="$out/deb/fbstudio_${version}_amd64"
    mkdir -p "$pkg/DEBIAN" "$pkg/opt/fbstudio" "$pkg/usr/bin" "$pkg/usr/share/applications" "$pkg/usr/share/icons/hicolor/256x256/apps"
    cp "$out/linux-x64/FbStudio" "$pkg/opt/fbstudio/FbStudio"
    chmod 755 "$pkg/opt/fbstudio/FbStudio"
    ln -s /opt/fbstudio/FbStudio "$pkg/usr/bin/fbstudio"
    cp src/FbStudio.App/Assets/fbstudio.png "$pkg/usr/share/icons/hicolor/256x256/apps/fbstudio.png"
    cat > "$pkg/usr/share/applications/fbstudio.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=FbStudio
Comment=IDE and administration tool for Firebird databases
Comment[es]=IDE y herramienta de administración para bases Firebird
Exec=fbstudio
Icon=fbstudio
Terminal=false
Categories=Development;Database;
Keywords=Firebird;SQL;database;
StartupWMClass=FbStudio
EOF
    cat > "$pkg/DEBIAN/control" <<EOF
Package: fbstudio
Version: $version
Section: database
Priority: optional
Architecture: amd64
Maintainer: Diego de la Fuente <52840662+diegodelafuenteDim@users.noreply.github.com>
Installed-Size: $(du -sk "$pkg" | cut -f1)
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66, libfontconfig1, libx11-6, libice6, libsm6
Recommends: libsecret-tools, gnome-keyring | kwalletmanager
Homepage: https://github.com/diegodelafuenteDim/FbStudio
Description: IDE and administration tool for Firebird databases
 SQL editor with autocompletion, a fast grid, data editing without locks,
 monitoring, trace, statistics, backup and restore, users and permissions,
 areas and diagrams. Passwords go to the desktop keyring (secret-tool).
EOF
    dpkg-deb --root-owner-group --build "$pkg" "$out/fbstudio_${version}_amd64.deb" > /dev/null
    rm -rf "$out/deb"
  else
    echo "Warning: dpkg-deb is not installed: no .deb package." >&2
  fi
fi

# macOS: one FbStudio.app bundle per architecture, with the files inside (the way macOS expects them).
if wants mac; then
  if [[ "$(uname)" != Darwin ]]; then
    echo "Warning: building the Mac packages outside a Mac: they will not be signed and Apple Silicon will not run them." >&2
  fi
  for rid in osx-arm64 osx-x64; do
    stage="$out/$rid/stage"
    publish "$rid" "$stage"
    chmod +x "$stage/FbStudio"
    if [[ "$(uname)" == Darwin ]]; then
      # Ad hoc signature for every Mach-O file (the program, createdump and the native libraries), signed one by one
      # before the bundle exists: inside FbStudio.app codesign would sign the whole bundle and ask for the .NET DLLs
      # to be signed too. Gatekeeper still asks the first time (there is no Apple signature), but it runs.
      while IFS= read -r -d '' f; do
        if file -b "$f" | grep -q Mach-O; then
          codesign --force --sign - "$f"
          codesign --verify "$f"
        fi
      done < <(find "$stage" -type f -print0)
    fi
    app="$out/$rid/FbStudio.app"
    mkdir -p "$app/Contents/Resources"
    mv "$stage" "$app/Contents/MacOS"
    cp src/FbStudio.App/Assets/fbstudio.icns "$app/Contents/Resources/"
    sed "s/@VERSION@/$version/g" tools/publish/Info.plist > "$app/Contents/Info.plist"
    (cd "$out/$rid" && zip -qry "$out/FbStudio-$version-$rid.zip" FbStudio.app)
  done
fi

# Sumas SHA256 de cada paquete, para comprobar que lo que se bajó es lo que se armó acá.
sums="SHA256SUMS-${targets// /-}.txt"
(
  cd "$out"
  files=$(ls *.zip *.tar.gz *.deb 2>/dev/null || true)
  if [[ -n "$files" ]]; then
    if command -v sha256sum > /dev/null; then sha256sum $files > "$sums"; else shasum -a 256 $files > "$sums"; fi
  fi
)

echo "Done:"
ls -lh "$out"/*.zip "$out"/*.tar.gz "$out"/*.deb "$out"/SHA256SUMS* 2>/dev/null || true
