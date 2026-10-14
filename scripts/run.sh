#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
stage build "$ROOT/scripts/build.sh"
stage application "$DOTNET" run --project src/SectorWorks.UI --configuration Release --no-build --no-restore -- "$@"
