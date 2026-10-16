#!/usr/bin/env bash
# Build Calcpad.Server in Debug, then copy the four runtime DLLs into the
# vscode-calcpad extension's bin/ folder.
#
# The PowerShell sibling of this script also handles killing a stuck bundled
# Calcpad.Server.exe before copying — that's a Windows-only problem (POSIX
# happily overwrites files in use), so this version just builds and copies.
# If you do see EBUSY-style failures on Linux/macOS, add a pkill step here.

set -euo pipefail

REPO_ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}"
CONFIGURATION="${2:-Debug}"

BACKEND_CSPROJ="$REPO_ROOT/Calcpad.Web/backend/Calcpad.Server.csproj"
BACKEND_BIN_DIR="$REPO_ROOT/Calcpad.Web/backend/bin/$CONFIGURATION/net10.0"
EXTENSION_ROOT="$REPO_ROOT/Calcpad.Web/frontend/vscode-calcpad"
EXTENSION_BIN="$EXTENSION_ROOT/bin"

echo "[deploy] building $BACKEND_CSPROJ ($CONFIGURATION)"
dotnet build "$BACKEND_CSPROJ" -c "$CONFIGURATION"

mkdir -p "$EXTENSION_BIN"

for dll in Calcpad.Core.dll Calcpad.Server.dll Calcpad.Highlighter.dll Calcpad.OpenXml.dll; do
    src="$BACKEND_BIN_DIR/$dll"
    if [ ! -f "$src" ]; then
        echo "Build produced no $dll at $src" >&2
        exit 1
    fi
    cp -f "$src" "$EXTENSION_BIN/"
done

echo "[deploy] OK - copied 4 DLLs to $EXTENSION_BIN"

# The extension's runtime is dist/extension.js, compiled from src/*.ts. If we
# only ship updated DLLs without recompiling TS, the running extension keeps
# the old request payloads / shapes and bug-fixes in extension.ts silently
# don't take effect. Always recompile after a DLL deploy so the bundle stays
# in sync.
echo "[deploy] compiling extension TypeScript -> dist/extension.js"
(cd "$EXTENSION_ROOT" && npm run compile)
echo "[deploy] OK - extension bundle rebuilt"
