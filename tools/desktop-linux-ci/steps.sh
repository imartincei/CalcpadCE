#!/usr/bin/env bash
# Runs inside the container; each argument is a step from run.sh.
set -euo pipefail
cd /src

: "${CALCPAD_VERSION:?CALCPAD_VERSION is not set}"
DESKTOP=Calcpad.Web/frontend/calcpad-desktop
arch=$(uname -m)
triple="$arch-unknown-linux-gnu"
bundle="$DESKTOP/src-tauri/target/$triple/release/bundle"
prefix="artifacts/CalcpadCE-desktop-$CALCPAD_VERSION-$arch"

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }

for s in "$@"; do
    case "$s" in
        deps)
            step "Install frontend dependencies"
            npm ci --prefix Calcpad.Web/frontend/calcpad-frontend
            npm ci --prefix Calcpad.Web/frontend/calcpad-web
            npm ci --prefix Calcpad.Web/frontend/calcpad-desktop
            ;;
        build)
            step "Build desktop bundles (Tauri)"
            rm -rf "$bundle"
            (cd "$DESKTOP" && bash build-desktop.sh)
            ;;
        test-x11)
            step "Run GTK window regression tests"
            timeout -k 30s 15m bash tools/desktop-test/linux-tests.sh
            ;;
        test-scale)
            step "Run GTK tests with display scaling"
            GDK_SCALE=2 timeout -k 30s 10m bash tools/desktop-test/linux-tests.sh --skip-build
            ;;
        test-wayland)
            step "Run GTK Wayland regression tests"
            timeout -k 30s 10m bash tools/desktop-test/linux-tests.sh --wayland --skip-build
            ;;
        stage)
            step "Stage artifacts"
            rm -rf artifacts && mkdir -p artifacts
            cp "$bundle"/deb/*.deb "$prefix.deb"
            cp "$bundle"/rpm/*.rpm "$prefix.rpm"
            if [ "$arch" = x86_64 ]; then
                cp "$DESKTOP"/packaging/arch/*.pkg.tar.zst "$prefix.pkg.tar.zst"
            fi
            ls -lh artifacts
            ;;
        verify)
            step "Verify third-party notices"
            shopt -s nullglob
            files=(artifacts/*)
            [ "${#files[@]}" -gt 0 ] || { echo "no staged artifacts; run the stage step" >&2; exit 1; }
            ./tools/license-scan/verify-notices.sh "${files[@]}"
            ;;
        *)
            echo "unknown step: $s" >&2
            exit 1
            ;;
    esac
done
