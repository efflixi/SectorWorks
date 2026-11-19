#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"

work="$(mktemp -d)"
trap 'rm -r -- "$work"' EXIT
appdir="$work/AppDir with spaces"
mkdir -p "$appdir/usr/lib/sectorworks"
cp packaging/linux/AppRun "$appdir/AppRun"
chmod 755 "$appdir/AppRun"
cat > "$appdir/usr/lib/sectorworks/SectorWorks" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$PWD" "$@"
exit 17
STUB
chmod 755 "$appdir/usr/lib/sectorworks/SectorWorks"
status=0
"$appdir/AppRun" "relative image.img" "--verify-ui" "" > "$work/actual" || status=$?
[[ "$status" == 17 ]]
printf '%s\n' "$ROOT" "relative image.img" "--verify-ui" "" > "$work/expected"
cmp "$work/expected" "$work/actual"
status=0
SECTORWORKS_APPIMAGETOOL="$work/missing" "$ROOT/scripts/appimage.sh" --check-tools > "$work/missing.log" 2>&1 || status=$?
[[ "$status" == 127 ]]
grep -q 'pinned tools missing' "$work/missing.log"
printf '#!/usr/bin/env bash\nexit 0\n' > "$work/wrong-tool"
chmod 755 "$work/wrong-tool"
printf 'not a runtime\n' > "$work/wrong-runtime"
status=0
SECTORWORKS_APPIMAGETOOL="$work/wrong-tool" SECTORWORKS_APPIMAGE_RUNTIME="$work/wrong-runtime" \
    "$ROOT/scripts/appimage.sh" --check-tools > "$work/hash.log" 2>&1 || status=$?
[[ "$status" == 1 ]]
grep -q 'checksum mismatch' "$work/hash.log"
printf "\nPASS: AppRun preserves spaces, empty/multiple arguments, caller directory and exit status; missing/tampered tools fail explicitly.\n"
