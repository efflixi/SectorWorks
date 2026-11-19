#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"

if [[ $# -ne 1 ]]; then
    printf "Usage: %s <linux-x64|linux-arm64|win-x64|win-arm64|all>\n" "$0" >&2
    exit 2
fi
case "$1" in
    all) targets=(linux-x64 linux-arm64 win-x64 win-arm64) ;;
    linux-x64|linux-arm64|win-x64|win-arm64) targets=("$1") ;;
    *) printf "FAIL [arguments]: unsupported runtime identifier: %s\n" "$1" >&2; exit 2 ;;
esac
if [[ "$1" == linux-x64 || "$1" == all ]]; then
    stage appimage-tools "$ROOT/scripts/appimage.sh" --check-tools
fi

stage restore "$DOTNET" restore src/SectorWorks.UI --locked-mode
publish_dir="$ROOT/artifacts/publish"
stage publish-directory mkdir -p "$publish_dir"
work="$(mktemp -d "$publish_dir/.publish-XXXXXX")"
trap 'rm -r -- "$work"' EXIT
for target in "${targets[@]}"; do
    destination="$publish_dir/$target"
    if [[ -L "$destination" || ( -e "$destination" && ! -d "$destination" ) ]]; then
        printf "FAIL [publish/%s]: expected an ordinary output directory at %s\n" "$target" "$destination" >&2
        exit 2
    fi
    stage "publish/$target" "$DOTNET" publish src/SectorWorks.UI --configuration Release \
        --runtime "$target" --self-contained true --no-restore -p:SectorWorksReleaseLayout=true \
        --output "$work/$target"
    stage "documentation-directories/$target" mkdir -p "$work/$target/docs" "$work/$target/licenses"
    stage "notices/$target" cp THIRD-PARTY-NOTICES.txt "$work/$target/docs/"
    stage "license/$target" cp LICENSE "$work/$target/licenses/"
    stage "licenses/$target" cp -R docs/licenses/. "$work/$target/licenses/"
    if [[ -d "$destination" ]]; then
        stage "replace/$target" mv -T -- "$destination" "$work/$target-previous"
    fi
    if ! mv -T -- "$work/$target" "$destination"; then
        if [[ -d "$work/$target-previous" ]] && ! mv -T -- "$work/$target-previous" "$destination"; then
            trap - EXIT
            printf "FAIL [publish/%s]: previous output is retained at %s.\n" "$target" "$work/$target-previous" >&2
        fi
        printf "FAIL [publish/%s]: could not install the finished publish directory.\n" "$target" >&2
        exit 1
    fi
    if [[ "$target" == linux-x64 ]]; then
        stage "appimage/$target" "$ROOT/scripts/appimage.sh"
    fi
done
printf "\nPASS: requested publishes completed; physical execution is separate.\n"
