#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"

if [[ $# -ne 1 ]]; then
    printf "Usage: %s <linux-x64|linux-arm64|win-x64|win-arm64|osx-x64|osx-arm64|all>\n" "$0" >&2
    exit 2
fi
case "$1" in
    all) targets=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64) ;;
    linux-x64|linux-arm64|win-x64|win-arm64|osx-x64|osx-arm64) targets=("$1") ;;
    *) printf "FAIL [arguments]: unsupported runtime identifier: %s\n" "$1" >&2; exit 2 ;;
esac
if [[ "$1" == linux-x64 || "$1" == all ]]; then
    stage appimage-tools "$ROOT/scripts/appimage.sh" --check-tools
fi

stage restore "$DOTNET" restore src/SectorWorks.UI --locked-mode
for target in "${targets[@]}"; do
    stage "publish/$target" "$DOTNET" publish src/SectorWorks.UI --configuration Release \
        --runtime "$target" --self-contained true --no-restore \
        --output "$ROOT/artifacts/publish/$target"
    stage "notices/$target" cp LICENSE THIRD-PARTY-NOTICES.txt "$ROOT/artifacts/publish/$target/"
    stage "license-directory/$target" mkdir -p "$ROOT/artifacts/publish/$target/licenses"
    stage "licenses/$target" cp -R docs/licenses/. "$ROOT/artifacts/publish/$target/licenses/"
    if [[ "$target" == linux-x64 ]]; then
        stage "appimage/$target" "$ROOT/scripts/appimage.sh"
    fi
done
printf "\nPASS: requested publishes completed; physical execution is separate.\n"
