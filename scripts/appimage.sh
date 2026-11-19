#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
source "$ROOT/scripts/appimage-tools.sh"

if [[ $# -gt 1 ]]; then
    printf "Usage: %s [output.AppImage|--check-tools]\n" "$0" >&2
    exit 2
fi
stage appimage-tools check_appimage_tools
if [[ "${1:-}" == --check-tools ]]; then exit 0; fi
output="${1:-$ROOT/artifacts/publish/SectorWorks-linux-x64.AppImage}"
output="$(readlink -m -- "$output")"
if [[ "$output" != *.AppImage || ! -d "$(dirname -- "$output")" ]]; then
    printf "FAIL [appimage-output]: specify an .AppImage file in an existing directory.\n" >&2
    exit 2
fi
source_dir="${SECTORWORKS_APPIMAGE_SOURCE:-$ROOT/artifacts/publish/linux-x64}"
if [[ -L "$source_dir" ]]; then
    printf "FAIL [appimage-input]: source directory must not be a symbolic link.\n" >&2
    exit 2
fi
source_dir="$(readlink -m -- "$source_dir")"
if [[ ! -x "$source_dir/SectorWorks" || ! -f "$source_dir/libs/SectorWorks.UI.deps.json" \
    || ! -f "$source_dir/libs/SectorWorks.UI.runtimeconfig.json" || ! -f "$source_dir/licenses/LICENSE" \
    || ! -f "$source_dir/docs/THIRD-PARTY-NOTICES.txt" ]]; then
    printf "FAIL [appimage-input]: complete self-contained linux-x64 publish and legal files required.\n" >&2
    exit 2
fi
for layer in Core Storage Services Platform UI; do
    if [[ ! -f "$source_dir/libs/SectorWorks.$layer.dll" ]]; then
        printf "FAIL [appimage-input]: missing application assembly: SectorWorks.%s.dll\n" "$layer" >&2
        exit 2
    fi
done
if [[ -n "$(find "$source_dir" -mindepth 1 -maxdepth 1 ! \( -name SectorWorks -o -name libs -o -name docs -o -name licenses \) -print -quit)" \
    || -n "$(find "$source_dir/docs" -mindepth 1 ! -name 'THIRD-PARTY-NOTICES.txt' -print -quit)" \
    || -n "$(find "$source_dir/libs" -type f ! \( -name '*.dll' -o -name '*.pdb' -o -name '*.so' -o -name '*.so.*' \
        -o -name 'createdump' -o -name 'SectorWorks.UI.deps.json' -o -name 'SectorWorks.UI.runtimeconfig.json' \) -print -quit)" ]]; then
    printf "FAIL [appimage-input]: unexpected files/directories in publish tree; refusing to bundle host data.\n" >&2
    exit 2
fi
if [[ -n "$(find "$source_dir" ! -type f ! -type d -print -quit)" \
    || -n "$(find "$source_dir" -type f \( -iname '*.img' -o -iname '*.opl' -o -iname '*.log' -o -iname '*.7z' -o -iname '*.pfx' -o -iname '*.key' \) -print -quit)" ]]; then
    printf "FAIL [appimage-input]: unexpected links, samples, runtime data or archives in publish tree.\n" >&2
    exit 2
fi
stage appimage-architecture bash -c 'file -- "$1" | grep -q "ELF 64-bit.*x86-64"' _ "$source_dir/SectorWorks"
work="$(mktemp -d "$ROOT/artifacts/publish/.appimage-XXXXXX")"
cleanup() {
    local status=$?
    if ! rm -r -- "$work"; then
        printf "FAIL [appimage-cleanup]: residual packaging directory: %s\n" "$work" >&2
        exit 1
    fi
    exit "$status"
}
trap cleanup EXIT
appdir="$work/SectorWorks.AppDir"
mkdir -p "$appdir/usr/lib/sectorworks" "$appdir/usr/bin" "$appdir/usr/share/applications" \
    "$appdir/usr/share/icons/hicolor/scalable/apps"
stage appimage-payload cp -a --no-preserve=ownership,xattr "$source_dir/." "$appdir/usr/lib/sectorworks/"
stage appimage-runtime-notices cp -R "$ROOT/packaging/linux/licenses" "$appdir/usr/lib/sectorworks/licenses/AppImage"
cp "$source_dir/licenses/LICENSE" "$appdir/LICENSE"
cat "$source_dir/docs/THIRD-PARTY-NOTICES.txt" "$ROOT/packaging/linux/licenses/SOURCES.txt" > "$appdir/THIRD-PARTY-NOTICES.txt"
cp "$ROOT/packaging/linux/AppRun" "$appdir/AppRun"
chmod 755 "$appdir/AppRun"
cp "$ROOT/packaging/linux/sectorworks.desktop" "$appdir/sectorworks.desktop"
cp "$ROOT/packaging/linux/sectorworks.desktop" "$appdir/usr/share/applications/"
cp "$ROOT/packaging/linux/sectorworks.svg" "$appdir/sectorworks.svg"
cp "$ROOT/packaging/linux/sectorworks.svg" "$appdir/usr/share/icons/hicolor/scalable/apps/"
ln -s sectorworks.svg "$appdir/.DirIcon"
ln -s ../lib/sectorworks/SectorWorks "$appdir/usr/bin/SectorWorks"
stage appimage-desktop desktop-file-validate "$appdir/sectorworks.desktop"
stage appimage-build env ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" \
    --no-appstream --runtime-file "$APPIMAGE_RUNTIME" \
    --mksquashfs-opt=-processors --mksquashfs-opt=2 --mksquashfs-opt=-no-xattrs \
    "$appdir" "$work/SectorWorks.AppImage"
chmod 755 "$work/SectorWorks.AppImage"
stage appimage-resource-check env APPIMAGE_EXTRACT_AND_RUN=1 "$work/SectorWorks.AppImage" --verify-ui
stage appimage-output mv -- "$work/SectorWorks.AppImage" "$output"
(cd -- "$(dirname -- "$output")" && sha256sum -- "$(basename -- "$output")" > "$(basename -- "$output").sha256")
printf "\nPASS: Linux x64 AppImage and checksum: %s\n" "$output"
