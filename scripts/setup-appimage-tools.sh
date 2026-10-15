#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
source "$ROOT/scripts/appimage-tools.sh"

if [[ $# -ne 0 ]]; then
    printf "Usage: %s\n" "$0" >&2
    exit 2
fi
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    printf "FAIL [appimage-tools]: installation requires Linux x86-64.\n" >&2
    exit 2
fi
mkdir -p "$APPIMAGE_TOOLS_DIR"
download() {
    local name="$1" url="$2" hash="$3" temporary
    if [[ -e "$APPIMAGE_TOOLS_DIR/$name" ]]; then
        printf '%s  %s\n' "$hash" "$APPIMAGE_TOOLS_DIR/$name" | sha256sum --check
        chmod 755 "$APPIMAGE_TOOLS_DIR/$name"
        return
    fi
    temporary="$(mktemp "$APPIMAGE_TOOLS_DIR/.download-XXXXXX")"
    if ! curl --fail --location --retry 3 --output "$temporary" "$url"; then
        rm -- "$temporary"
        return 1
    fi
    if ! printf '%s  %s\n' "$hash" "$temporary" | sha256sum --check; then
        rm -- "$temporary"
        printf "FAIL [appimage-tools]: upstream bytes differ from the approved pin; review before updating.\n" >&2
        return 1
    fi
    chmod 755 "$temporary"
    mv -- "$temporary" "$APPIMAGE_TOOLS_DIR/$name"
}
stage appimagetool-download download appimagetool-x86_64.AppImage \
    https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage "$APPIMAGE_TOOL_SHA256"
stage appimage-runtime-download download runtime-x86_64 \
    https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64 "$APPIMAGE_RUNTIME_SHA256"
stage appimage-tools-check check_appimage_tools
stage appimagetool-version env APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" --version
printf "\nPASS: pinned AppImage tools installed user-locally; no system packages changed.\n"
