#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
stage shell-syntax bash -n scripts/common.sh scripts/restore.sh scripts/build.sh \
    scripts/test.sh scripts/verify.sh scripts/publish.sh scripts/run.sh \
    scripts/appimage-tools.sh scripts/setup-appimage-tools.sh scripts/appimage.sh \
    scripts/test-appimage.sh packaging/linux/AppRun
stage appimage-contracts "$ROOT/scripts/test-appimage.sh"
stage technical-spec-copy cmp Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt \
    docs/specifications/Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt
stage ui-spec-copy cmp SectorWorks_UI_Look_and_Feel_Spec_v0.2.txt \
    docs/specifications/SectorWorks_UI_Look_and_Feel_Spec_v0.2.txt
stage design-guide-copy cmp SectorWorks_UI_Design_and_Prototype_Guide_v0.2.docx \
    docs/design/SectorWorks_UI_Design_and_Prototype_Guide_v0.2.docx
stage mockup-01-copy cmp UI01.png docs/design/UI01.png
stage mockup-02-copy cmp UI02.png docs/design/UI02.png
stage tests "$ROOT/scripts/test.sh"
stage format "$DOTNET" format SectorWorks.sln --verify-no-changes --no-restore
stage ui-verification "$DOTNET" run --project src/SectorWorks.UI --configuration Release --no-build --no-restore -- --verify-ui
printf "\nPASS: local verification (physical platform validation is tracked separately).\n"
