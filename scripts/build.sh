#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
stage restore "$ROOT/scripts/restore.sh"
stage build "$DOTNET" build SectorWorks.sln --configuration Release --no-restore "$@"
