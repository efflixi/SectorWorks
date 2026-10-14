#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
stage restore "$DOTNET" restore SectorWorks.sln --locked-mode "$@"
