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
#      macOS, deb/AppImage on Linux) into src-tauri/target/release/bundle/.
#
# Usage:
#   ./build-desktop.sh [--rid=<rid>] [--target=<triple>] [--bundles=<list>]
#     --rid: dotnet publish RID (default: host)
#     --target: cargo target triple for `tauri build --target` (default: host)
#     --bundles: comma-separated tauri bundle types, e.g. deb,appimage,dmg
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

# .NET ships libcoreclrtraceptprovider.so for optional LTTng tracing. It pulls
# liblttng-ust.so.0, which linuxdeploy fails to resolve on distros that ship
# lttng-ust 2.13+ (Arch/CachyOS have .so.1 only). Runtime doesn't need it.
# Also purge the copy tauri staged into target/ on a prior build — cargo's
# incremental step won't re-stage resources, so a stale copy would survive.
if [[ "$CARGO_TARGET" == *linux* ]]; then
    rm -f "$BINARIES_DIR/libcoreclrtraceptprovider.so"
    if [[ -d "$SCRIPT_DIR/src-tauri/target" ]]; then
        find "$SCRIPT_DIR/src-tauri/target" -name libcoreclrtraceptprovider.so -delete 2>/dev/null || true
    fi
fi

cd "$SCRIPT_DIR"

# NO_STRIP: linuxdeploy's bundled `strip` (binutils ~2.34) can't parse the
# SHT_RELR (`.relr.dyn`) sections modern glibc/Arch libraries ship, and aborts
# with "unknown type [0x13]". Skip stripping — the AppImage stays a bit larger
# but bundles successfully.
if [[ "$CARGO_TARGET" == *linux* ]]; then
    export NO_STRIP=true
fi

if [[ -n "$BUNDLES" ]]; then
    IFS=',' read -ra BUNDLE_LIST <<< "$BUNDLES"
    npx tauri build --config src-tauri/tauri.linux.conf.json --target "$CARGO_TARGET" --bundles "${BUNDLE_LIST[@]}"
    BUNDLED_APPIMAGE=false
    for b in "${BUNDLE_LIST[@]}"; do [[ "$b" == "appimage" ]] && BUNDLED_APPIMAGE=true; done
else
    npx tauri build --config src-tauri/tauri.linux.conf.json --target "$CARGO_TARGET"
    BUNDLED_APPIMAGE=true
fi

# linuxdeploy-plugin-gtk hardcodes GTK_THEME to Adwaita:{light|dark} based on
# a `gsettings get org.gnome.desktop.interface gtk-theme` grep for "dark".
# That misses GNOME 42+ (which signals dark via color-scheme=prefer-dark while
# gtk-theme stays "Adwaita") and returns empty on non-GNOME desktops, so the
# menu bar ends up permanently light. Swap in a smarter hook that consults
# color-scheme, KDE settings, and freedesktop portals, then repack.
if [[ "$CARGO_TARGET" == *linux* && "$BUNDLED_APPIMAGE" == "true" ]]; then
    APPIMAGE_DIR="$SCRIPT_DIR/src-tauri/target/$CARGO_TARGET/release/bundle/appimage"
    APPDIR=$(find "$APPIMAGE_DIR" -maxdepth 1 -type d -name '*.AppDir' | head -n1)
    APPIMG=$(find "$APPIMAGE_DIR" -maxdepth 1 -type f -name '*.AppImage' | head -n1)
    if [[ -n "$APPDIR" && -n "$APPIMG" && -f "$APPDIR/apprun-hooks/linuxdeploy-plugin-gtk.sh" ]]; then
        echo ">> Patching AppImage GTK theme detection"
        APPIMAGE_ARCH="${CARGO_TARGET%%-*}"
        cat > "$APPDIR/apprun-hooks/linuxdeploy-plugin-gtk.sh" <<'HOOK_EOF'
#! /usr/bin/env bash
# Replaces the default linuxdeploy GTK hook. Picks Adwaita:dark whenever any
# common signal says the user prefers dark mode, otherwise Adwaita:light.
detect_dark() {
    [[ "$GTK_THEME" == *dark* || "$GTK_THEME" == *:dark ]] && return 0
    if command -v gsettings >/dev/null 2>&1; then
        local cs gt
        cs=$(gsettings get org.gnome.desktop.interface color-scheme 2>/dev/null)
        [[ "$cs" == *prefer-dark* ]] && return 0
        gt=$(gsettings get org.gnome.desktop.interface gtk-theme 2>/dev/null)
        [[ "$gt" == *[Dd]ark* ]] && return 0
    fi
    local kde="${XDG_CONFIG_HOME:-$HOME/.config}/kdeglobals"
    if [[ -f "$kde" ]] && grep -qiE '^ColorScheme=.*Dark' "$kde"; then
        return 0
    fi
    if command -v busctl >/dev/null 2>&1; then
        local portal
        portal=$(busctl --user --json=short call org.freedesktop.portal.Desktop \
            /org/freedesktop/portal/desktop org.freedesktop.portal.Settings Read \
            ss org.freedesktop.appearance color-scheme 2>/dev/null)
        [[ "$portal" == *'"u"'*'"1"'* || "$portal" == *'variant":"u","data":1'* ]] && return 0
    fi
    return 1
}

if detect_dark; then GTK_THEME_VARIANT="dark"; else GTK_THEME_VARIANT="light"; fi
APPIMAGE_GTK_THEME="${APPIMAGE_GTK_THEME:-"Adwaita:$GTK_THEME_VARIANT"}"

export APPDIR="${APPDIR:-"$(dirname "$(realpath "$0")")"}"
export GTK_DATA_PREFIX="$APPDIR"
export GTK_THEME="$APPIMAGE_GTK_THEME"
export GDK_BACKEND=x11
export XDG_DATA_DIRS="$APPDIR/usr/share:/usr/share:$XDG_DATA_DIRS"
export GSETTINGS_SCHEMA_DIR="$APPDIR/usr/share/glib-2.0/schemas"
export GTK_EXE_PREFIX="$APPDIR/usr"
export GTK_PATH="$APPDIR/usr/lib/gtk-3.0:/usr/lib64/gtk-3.0:/usr/lib/x86_64-linux-gnu/gtk-3.0:/usr/lib/aarch64-linux-gnu/gtk-3.0"
export GTK_IM_MODULE_FILE="$APPDIR/usr/lib/gtk-3.0/3.0.0/immodules.cache"
export GDK_PIXBUF_MODULE_FILE="$APPDIR/usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"
export GIO_EXTRA_MODULES="$APPDIR/usr/lib/gio/modules"
HOOK_EOF
        rm -f "$APPIMG"
        (cd "$APPIMAGE_DIR" && \
            ARCH="$APPIMAGE_ARCH" NO_STRIP=true \
            "$HOME/.cache/tauri/linuxdeploy-plugin-appimage.AppImage" \
            --appimage-extract-and-run --appdir "$APPDIR")
        REPACKED=$(find "$APPIMAGE_DIR" -maxdepth 1 -type f -name '*.AppImage' | head -n1)
        if [[ -n "$REPACKED" && "$REPACKED" != "$APPIMG" ]]; then
            mv "$REPACKED" "$APPIMG"
        fi
        echo ">> AppImage repacked with patched GTK hook"
    fi
fi
