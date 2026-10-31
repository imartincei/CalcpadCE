#!/usr/bin/env bash
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
DESKTOP="$HERE/../../Calcpad.Web/frontend/calcpad-desktop"
WORK="$HERE/.work/linux-tests"
mkdir -p "$WORK/app/bin" "$WORK/app/lib/CalcpadCE Linux Tests"

if [ "${1:-}" = --run ]; then
    shift
    XDG_RUNTIME_DIR=$(mktemp -d /tmp/calcpad-gtk-XXXXXX)
    export XDG_RUNTIME_DIR
    if [ "${CALCPAD_TEST_BACKEND:-x11}" = wayland ]; then
        export GDK_BACKEND=wayland
        unset DISPLAY WAYLAND_DISPLAY
        WLR_BACKENDS=headless WLR_RENDERER=pixman WLR_LIBINPUT_NO_DEVICES=1 \
            sway --config "$HERE/sway-test.conf" > "$WORK/sway.log" 2>&1 &
    else
        export GDK_BACKEND=x11
        unset WAYLAND_DISPLAY
        openbox --sm-disable > "$WORK/openbox.log" 2>&1 &
    fi
    window_manager=$!
    trap 'kill "$window_manager"' EXIT
    if [ "$GDK_BACKEND" = wayland ]; then
        for attempt in {1..100}; do
            for candidate in "$XDG_RUNTIME_DIR"/wayland-*; do
                if [ -S "$candidate" ]; then export WAYLAND_DISPLAY="${candidate##*/}"; fi
            done
            for candidate in "$XDG_RUNTIME_DIR"/sway-ipc.*.sock; do
                if [ -S "$candidate" ]; then export SWAYSOCK="$candidate"; fi
            done
            if [ -n "${WAYLAND_DISPLAY:-}" ] && [ -n "${SWAYSOCK:-}" ]; then break; fi
            kill -0 "$window_manager"
            sleep 0.1
        done
        : "${WAYLAND_DISPLAY:?Wayland compositor did not start}"
        : "${SWAYSOCK:?Sway IPC did not start}"
    fi
    node --test "$@" "$HERE/linux.test.mjs"
    exit
fi

backend=x11
skip_build=false
while [ "$#" -gt 0 ]; do
    case "$1" in
        --wayland) backend=wayland; shift ;;
        --skip-build) skip_build=true; shift ;;
        *) break ;;
    esac
done

if [ "$skip_build" = false ]; then
    if [ ! -f "$DESKTOP/src-tauri/binaries/Calcpad.Server" ]; then
        bash "$DESKTOP/stage-sidecar.sh"
    fi
    npm --prefix "$DESKTOP/../calcpad-frontend" run build
    npm --prefix "$DESKTOP/../calcpad-web" run build:desktop
    export TAURI_CONFIG='{"identifier":"org.calcpad-ce.linux-tests","productName":"CalcpadCE Linux Tests","bundle":{"resources":[]},"app":{"security":{"capabilities":["default",{"identifier":"linux-tests","windows":["main*"],"permissions":["core:window:allow-set-position","core:window:allow-set-size"]}]}}}'
    cargo build --locked --manifest-path "$DESKTOP/src-tauri/Cargo.toml" --target-dir "$WORK/target" --bin calcpad-desktop
    cp "$WORK/target/debug/calcpad-desktop" "$WORK/app/bin/calcpad-linux-tests"
    cp -a "$DESKTOP/src-tauri/binaries/." "$WORK/app/lib/CalcpadCE Linux Tests/"
fi

if [ "$backend" = wayland ]; then
    exec dbus-run-session -- env CALCPAD_TEST_BACKEND=wayland bash "$HERE/linux-tests.sh" --run "$@"
fi
exec xvfb-run -a -s '-screen 0 6400x3600x24 -nolisten tcp' \
    dbus-run-session -- env CALCPAD_TEST_BACKEND=x11 bash "$HERE/linux-tests.sh" --run "$@"
