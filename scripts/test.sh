#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
stage build "$ROOT/scripts/build.sh"
stage test "$DOTNET" test SectorWorks.sln --configuration Release --no-build --no-restore "$@"
