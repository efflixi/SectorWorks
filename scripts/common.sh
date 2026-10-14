#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
STAGE="setup"
trap 'status=$?; printf "FAIL [%s] exit=%s\n" "$STAGE" "$status" >&2; exit "$status"' ERR

if [[ -n "${SECTORWORKS_DOTNET:-}" ]]; then
    DOTNET="$SECTORWORKS_DOTNET"
elif command -v dotnet >/dev/null 2>&1; then
    DOTNET="$(command -v dotnet)"
elif [[ -x "$HOME/.local/share/SectorWorks/dotnet/dotnet" ]]; then
    DOTNET="$HOME/.local/share/SectorWorks/dotnet/dotnet"
else
    printf "FAIL [setup]: .NET SDK 10.0.402 is required. See README.md.\n" >&2
    exit 127
fi
if [[ ! -x "$DOTNET" ]]; then
    printf "FAIL [setup]: dotnet executable not found: %s\n" "$DOTNET" >&2
    exit 127
fi
DOTNET="$(readlink -f -- "$DOTNET")"
export DOTNET_ROOT
DOTNET_ROOT="$(dirname -- "$DOTNET")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

stage() {
    STAGE="$1"
    shift
    printf "\n== %s ==\n" "$STAGE"
    "$@"
}
