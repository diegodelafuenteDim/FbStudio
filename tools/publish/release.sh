#!/usr/bin/env bash
# Publica una versión en GitHub Releases con los paquetes que armó el CI (Windows, Linux y Mac, juntos en una carpeta):
# los comprueba contra el SHA256SUMS de cada trabajo, suma el .deb también con nombre fijo (fbstudio_amd64.deb, para
# .../releases/latest/download/fbstudio_amd64.deb) y un solo SHA256SUMS.txt.
#
#   tools/publish/release.sh v0.1.0 dist            # necesita GH_TOKEN y GH_REPO (el CI los pone)
#   DRY_RUN=1 tools/publish/release.sh v0.1.0 dist  # prepara y muestra, no publica nada
set -euo pipefail

tag="${1:?tag, e.g. v0.1.0}"
dir="${2:?folder with the packages}"
if [[ ! "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.]+)?$ ]]; then
  echo "Invalid tag: $tag" >&2
  exit 1
fi
version="${tag#v}"

# Lo nuevo de esta versión, de CHANGELOG.md: sin su sección no se publica (cada versión dice qué cambió).
changelog="${CHANGELOG:-CHANGELOG.md}"
changes="$(awk -v head="## [$version]" '
  index($0, "## [") == 1 { inside = (index($0, head) == 1); next }
  inside { print }' "$changelog" 2>/dev/null | sed '/./,$!d' | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}')"
if ! grep -q '^- ' <<< "$changes"; then
  echo "CHANGELOG.md has no changes for $version: add a \"## [$version] - date\" section (### English and ### Español)" >&2
  exit 1
fi

cd "$dir"

# Lo que llegó acá es lo que armó cada trabajo.
sha256sum -c SHA256SUMS-*.txt

# Todos los paquetes, con la versión de la etiqueta (si no coincide con <Version> de Directory.Build.props, no están).
expected=(
  "FbStudio-$version-win-x64.zip"
  "FbStudio-$version-linux-x64.tar.gz"
  "fbstudio_${version}_amd64.deb"
  "FbStudio-$version-osx-arm64.zip"
  "FbStudio-$version-osx-x64.zip"
)
for f in "${expected[@]}"; do
  if [[ ! -f "$f" ]]; then
    echo "Missing $f: does the tag match <Version> in Directory.Build.props?" >&2
    exit 1
  fi
done

cp "fbstudio_${version}_amd64.deb" fbstudio_amd64.deb
files=("${expected[@]}" fbstudio_amd64.deb)
sha256sum "${files[@]}" > SHA256SUMS.txt
cat SHA256SUMS.txt

notes=$(cat <<EOF
**Downloads** (no .NET install needed)

- **Windows:** \`FbStudio-$version-win-x64.zip\`: extract it and run \`FbStudio.exe\`. It is not signed, so the first time Windows may show "Windows protected your PC": **More info** → **Run anyway**.
- **Linux (Debian, Ubuntu):** \`fbstudio_amd64.deb\`: \`sudo apt install ./fbstudio_amd64.deb\`, then \`fbstudio\` or the menu entry. Other distributions: \`FbStudio-$version-linux-x64.tar.gz\`.
- **macOS:** \`FbStudio-$version-osx-arm64.zip\` (Apple Silicon) or \`FbStudio-$version-osx-x64.zip\` (Intel). The first time: System Settings → Privacy & Security → **Open Anyway**.

To check a download: \`sha256sum -c SHA256SUMS.txt --ignore-missing\`.
EOF
)
notes+=$'\n\n'"## What's new · Novedades"$'\n\n'"$changes"

# Una versión con sufijo (0.2.0-beta.1) sale como pre-release.
prerelease=()
if [[ "$version" == *-* ]]; then prerelease=(--prerelease); fi

if [[ "${DRY_RUN:-}" == 1 ]]; then
  echo "DRY_RUN: would create the release $tag (FbStudio $version${prerelease[*]:+, pre-release}) with:"
  printf '  %s\n' "${files[@]}" SHA256SUMS.txt
  echo "$notes"
  exit 0
fi

gh release create "$tag" "${files[@]}" SHA256SUMS.txt --verify-tag --title "FbStudio $version" \
  --notes "$notes" --generate-notes ${prerelease[@]+"${prerelease[@]}"}
