#!/usr/bin/env bash

APPIMAGE_TOOL_SHA256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
APPIMAGE_RUNTIME_SHA256=156f4bdbde9c52d01814600013e0a273f0118dc2de98975f3c8c63427ec79074
APPIMAGE_TOOLS_DIR="$HOME/.local/share/SectorWorks/appimage-tools/1.9.1-8f39b89"
APPIMAGETOOL="${SECTORWORKS_APPIMAGETOOL:-$APPIMAGE_TOOLS_DIR/appimagetool-x86_64.AppImage}"
APPIMAGE_RUNTIME="${SECTORWORKS_APPIMAGE_RUNTIME:-$APPIMAGE_TOOLS_DIR/runtime-x86_64}"

check_appimage_tools() {
    if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
        printf "FAIL [appimage-tools]: packaging requires a Linux x86-64 host.\n" >&2
        return 2
    fi
    for tool in sha256sum desktop-file-validate file; do
        if ! command -v "$tool" >/dev/null 2>&1; then
            printf "FAIL [appimage-tools]: required command missing: %s\n" "$tool" >&2
            return 127
        fi
    done
    if [[ ! -x "$APPIMAGETOOL" || ! -f "$APPIMAGE_RUNTIME" ]]; then
        printf "FAIL [appimage-tools]: pinned tools missing. Run ./scripts/setup-appimage-tools.sh once. No tools are downloaded by publish.\n" >&2
        return 127
    fi
    if ! printf '%s  %s\n' "$APPIMAGE_TOOL_SHA256" "$APPIMAGETOOL" \
        "$APPIMAGE_RUNTIME_SHA256" "$APPIMAGE_RUNTIME" | sha256sum --check; then
        printf "FAIL [appimage-tools]: tool/runtime checksum mismatch; review the pinned installation.\n" >&2
        return 1
    fi
}
