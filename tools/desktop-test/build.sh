#!/usr/bin/env bash
# Builds an isolated debug copy of the desktop app under .work/ (Linux counterpart of build.ps1).
#   --stage     re-publish Calcpad.Server into src-tauri/binaries first (after backend changes)
#   --frontend  rebuild calcpad-frontend + calcpad-web dist first (after TS changes)
# The copy uses identifier com.calcpadce.desktop.test, so it has its own app data folder and
# single-instance lock and never touches the developer's own CalcpadCE.
set -euo pipefail
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
WORK="$HERE/.work"
DESKTOP=$(cd "$HERE/../../Calcpad.Web/frontend/calcpad-desktop" && pwd)
mkdir -p "$WORK"

for arg in "$@"; do
    case "$arg" in
        --stage) "$DESKTOP/stage-sidecar.sh" ;;
        --frontend) "$DESKTOP/build-frontend.sh" ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

export CARGO_TARGET_DIR="$WORK/tauri-target"
export TAURI_CONFIG='{"identifier":"com.calcpadce.desktop.test","productName":"CalcpadCE Test"}'
(cd "$DESKTOP/src-tauri" && cargo build)

# Laid out like the .deb install, so Tauri finds the server in ../lib/<productName> as it does in
# production. Assembled fresh so files from an older publish can't skew a test.
BINARIES="$DESKTOP/src-tauri/binaries"
SERVER_DIR="$WORK/app/lib/CalcpadCE Test"
[ -x "$BINARIES/Calcpad.Server" ] || { echo "No server in $BINARIES; rerun with --stage" >&2; exit 1; }
"$HERE/launch.sh" --stop >/dev/null
rm -rf "$WORK/app"
mkdir -p "$WORK/app/bin" "$SERVER_DIR"
cp -r "$BINARIES"/. "$SERVER_DIR/"
cp "$WORK/tauri-target/debug/calcpad-desktop" "$WORK/app/bin/"
cp -f "$SERVER_DIR/appsettings.json" "$WORK/appsettings.default.json"
echo "Built $WORK/app/bin/calcpad-desktop"
