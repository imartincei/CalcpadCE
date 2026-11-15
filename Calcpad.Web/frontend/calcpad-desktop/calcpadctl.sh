#!/bin/sh
# Convenience wrapper for the desktop control channel (see src-tauri/src/control.rs).
set -eu

APP="${CALCPAD_DESKTOP:-calcpad-desktop}"
APP_CLASS=$(basename "$APP")

case "${1:-}" in
    screenshot)
        shift
        output="${1:-}"
        if [ -z "$output" ]; then
            echo "usage: calcpadctl screenshot <file.png>" >&2
            exit 2
        fi
        if ! command -v spectacle >/dev/null 2>&1; then
            echo "calcpadctl: spectacle is required for screenshot" >&2
            exit 1
        fi
        # On Wayland the app cannot raise itself; KWin's kdotool can.
        if command -v kdotool >/dev/null 2>&1; then
            window=$(kdotool search --class "$APP_CLASS" | head -n 1)
            if [ -z "$window" ]; then
                window=$(kdotool search --class calcpad-desktop | head -n 1)
            fi
            if [ -n "$window" ]; then
                kdotool windowactivate "$window"
            fi
        else
            "$APP" --ctl focus >/dev/null || true
        fi
        sleep 0.4
        exec spectacle -a -b -n -o "$output"
        ;;
    "")
        exec "$APP" --ctl
        ;;
    --ctl|--ctl-json)
        exec "$APP" "$@"
        ;;
    *)
        exec "$APP" --ctl "$@"
        ;;
esac
