#!/usr/bin/env bash
# Publish Calcpad.Server for the host RID and mirror the whole publish tree
# into src-tauri/target/{debug,release}/ so BaseDirectory::Resource resolves
# to a directory that already contains the apphost plus every sibling DLL /
# native lib / deps.json / runtimeconfig.json at `tauri dev` time.
#
# The .NET apphost is framework-dependent-style: it expects Calcpad.Server.dll
# and ~200 sibling files in the SAME directory as itself. `tauri build`
# handles the equivalent for bundled installers via `bundle.resources` in
# tauri.conf.json — this script is only for the dev loop.
#
# `bundle.externalBin` was removed in favor of spawning the apphost directly
# via tokio::process::Command from the resource dir. See spawn_sidecar in
# src-tauri/src/lib.rs for why.

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../../.." && pwd)
SYNC_SCRIPT="$REPO_ROOT/Calcpad.Web/frontend/vscode-calcpad/scripts/sync-bundled-server.mjs"
BINARIES_DIR="$SCRIPT_DIR/src-tauri/binaries"

TRIPLE=$(rustc -vV | awk '/^host:/ {print $2}')

case "$TRIPLE" in
    x86_64-unknown-linux-gnu)   RID="linux-x64" ;;
    aarch64-unknown-linux-gnu)  RID="linux-arm64" ;;
    x86_64-apple-darwin)        RID="osx-x64" ;;
    aarch64-apple-darwin)       RID="osx-arm64" ;;
    x86_64-pc-windows-msvc)     RID="win-x64" ;;
    aarch64-pc-windows-msvc)    RID="win-arm64" ;;
    *) echo "unsupported host triple: $TRIPLE" >&2; exit 1 ;;
esac

echo ">> Cargo target: $TRIPLE"
echo ">> .NET RID:     $RID"

mkdir -p "$BINARIES_DIR"
node "$SYNC_SCRIPT" \
    --target="$BINARIES_DIR" \
    --rid="$RID" \
    --configuration=Release \
    --keep-skia-natives

# After the sync: it prunes anything it does not recognise from the target.
cp "$REPO_ROOT/THIRD-PARTY-NOTICES.txt" "$BINARIES_DIR/"
cp "$REPO_ROOT/LICENSE" "$BINARIES_DIR/LICENSE.txt"

if [[ "$TRIPLE" == *windows* ]]; then
    APPHOST="$BINARIES_DIR/Calcpad.Server.exe"
else
    APPHOST="$BINARIES_DIR/Calcpad.Server"
fi
if [[ ! -f "$APPHOST" ]]; then
    echo "!! Expected published apphost at $APPHOST" >&2
    exit 1
fi
echo ">> Publish tree staged at $BINARIES_DIR"

for profile in debug release; do
    prof_dir="$SCRIPT_DIR/src-tauri/target/$profile"
    if [[ -d "$prof_dir" ]]; then
        cp -R "$BINARIES_DIR"/. "$prof_dir/"
        echo ">> Mirrored publish tree into $prof_dir/"
    fi
done
