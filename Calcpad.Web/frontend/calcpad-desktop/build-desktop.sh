#!/usr/bin/env bash
#
# Build the CalcpadCE desktop app (Tauri + embedded ASP.NET Core sidecar).
#
# Steps:
#   1. Publish Calcpad.Server for the target RID via sync-bundled-server.mjs.
#   2. Rename the published apphost to Tauri's target-triple suffix so
#      `bundle.externalBin` finds it (see src-tauri/tauri.conf.json).
#   3. Delegate the actual frontend build + bundling to `tauri build`, which
#      invokes vite and produces installers (msi/nsis on Windows, dmg on
#      macOS, deb/rpm on Linux) into src-tauri/target/release/bundle/.
#
# Usage:
#   ./build-desktop.sh [--rid=<rid>] [--target=<triple>] [--bundles=<list>]
#     --rid: dotnet publish RID (default: host)
#     --target: cargo target triple for `tauri build --target` (default: host)
#     --bundles: comma-separated tauri bundle types, e.g. deb,rpm,dmg
#                (default: all formats configured for the platform)

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../../.." && pwd)
SYNC_SCRIPT="$REPO_ROOT/Calcpad.Web/frontend/vscode-calcpad/scripts/sync-bundled-server.mjs"
BINARIES_DIR="$SCRIPT_DIR/src-tauri/binaries"

DOTNET_RID=""
CARGO_TARGET=""
BUNDLES=""
for arg in "$@"; do
    case "$arg" in
        --rid=*)     DOTNET_RID="${arg#*=}" ;;
        --target=*)  CARGO_TARGET="${arg#*=}" ;;
        --bundles=*) BUNDLES="${arg#*=}" ;;
        *)           echo "Unknown option: $arg" >&2; exit 1 ;;
    esac
done

detect_host_triple() {
    local uname_s uname_m
    uname_s=$(uname -s)
    uname_m=$(uname -m)
    case "$uname_s" in
        Linux*)
            case "$uname_m" in
                x86_64)  echo "x86_64-unknown-linux-gnu" ;;
                aarch64) echo "aarch64-unknown-linux-gnu" ;;
                *) echo "unsupported Linux arch: $uname_m" >&2; return 1 ;;
            esac ;;
        Darwin*)
            case "$uname_m" in
                x86_64) echo "x86_64-apple-darwin" ;;
                arm64)  echo "aarch64-apple-darwin" ;;
                *) echo "unsupported macOS arch: $uname_m" >&2; return 1 ;;
            esac ;;
        MINGW*|MSYS*|CYGWIN*)
            echo "x86_64-pc-windows-msvc" ;;
        *) echo "unsupported OS: $uname_s" >&2; return 1 ;;
    esac
}

triple_to_rid() {
    case "$1" in
        x86_64-unknown-linux-gnu)   echo "linux-x64" ;;
        aarch64-unknown-linux-gnu)  echo "linux-arm64" ;;
        x86_64-apple-darwin)        echo "osx-x64" ;;
        aarch64-apple-darwin)       echo "osx-arm64" ;;
        x86_64-pc-windows-msvc)     echo "win-x64" ;;
        aarch64-pc-windows-msvc)    echo "win-arm64" ;;
        *) echo "unsupported target triple: $1" >&2; return 1 ;;
    esac
}

HOST_TRIPLE=$(detect_host_triple)
[[ -z "$CARGO_TARGET" ]] && CARGO_TARGET="$HOST_TRIPLE"
[[ -z "$DOTNET_RID"   ]] && DOTNET_RID=$(triple_to_rid "$CARGO_TARGET")

echo ">> Cargo target: $CARGO_TARGET"
echo ">> .NET RID:     $DOTNET_RID"

mkdir -p "$BINARIES_DIR"
node "$SYNC_SCRIPT" \
    --target="$BINARIES_DIR" \
    --rid="$DOTNET_RID" \
    --configuration=Release \
    --keep-skia-natives

# Tauri looks up sidecars by `<name>-<target-triple>[.exe]`.
if [[ "$CARGO_TARGET" == *windows* ]]; then
    SRC_EXE="$BINARIES_DIR/Calcpad.Server.exe"
    DEST_EXE="$BINARIES_DIR/calcpad-server-$CARGO_TARGET.exe"
else
    SRC_EXE="$BINARIES_DIR/Calcpad.Server"
    DEST_EXE="$BINARIES_DIR/calcpad-server-$CARGO_TARGET"
fi
if [[ ! -f "$SRC_EXE" ]]; then
    echo "!! Expected published apphost at $SRC_EXE" >&2
    exit 1
fi
cp "$SRC_EXE" "$DEST_EXE"
chmod +x "$DEST_EXE"
echo ">> Sidecar staged at $DEST_EXE"

cd "$SCRIPT_DIR"

if [[ -n "$BUNDLES" ]]; then
    IFS=',' read -ra BUNDLE_LIST <<< "$BUNDLES"
    npx tauri build --config src-tauri/tauri.linux.conf.json --target "$CARGO_TARGET" --bundles "${BUNDLE_LIST[@]}"
else
    npx tauri build --config src-tauri/tauri.linux.conf.json --target "$CARGO_TARGET"
fi
