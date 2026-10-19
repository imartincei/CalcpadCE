#!/usr/bin/env bash
# Starts the isolated test app (see build.sh) on an off-screen KWin (Linux counterpart of launch.ps1).
#   launch.sh [--settings <appsettings.json path>] [--env NAME=value]... [--port <port>]
#   launch.sh --stop
# Restarts the test app if it is running. Without --settings the server gets its stock appsettings.json.
# Output from the app, its server and KWin goes to .work/app-out.txt. The webview's inspector
# listens on --port (see cdp.mjs).
set -euo pipefail
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
WORK="$HERE/.work"
APP="$WORK/app"
EXE="$APP/bin/calcpad-desktop"
SERVER_DIR="$APP/lib/CalcpadCE Test"
SOCKET=calcpad-desktop-test
settings='' port=9223 stop=0 envs=()
while [ $# -gt 0 ]; do
    case "$1" in
        --settings) settings=$2; shift 2 ;;
        --env) envs+=("$2"); shift 2 ;;
        --port) port=$2; shift 2 ;;
        --stop) stop=1; shift ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

# Only processes started from .work/app (the app, its server and any PDF browser it downloaded
# there), then the KWin they ran on. Never the developer's own CalcpadCE.
stop_test_app() {
    pkill -TERM -f "^$APP/" 2>/dev/null || true
    for _ in $(seq 1 30); do
        pgrep -f "^$APP/" >/dev/null || break
        sleep 0.1
    done
    pkill -KILL -f "^$APP/" 2>/dev/null || true
    pkill -TERM -f "^kwin_wayland --virtual --socket $SOCKET" 2>/dev/null || true
    sleep 0.3
}

stop_test_app
[ "$stop" = 1 ] && { echo stopped; exit 0; }
[ -x "$EXE" ] || { echo "Test app not built; run build.sh first" >&2; exit 1; }
cp -f "${settings:-$WORK/appsettings.default.json}" "$SERVER_DIR/appsettings.json"

# The session gets none of the developer's displays or D-Bus, so nothing can reach their desktop.
# WebKit's DMA-BUF renderer trips a Wayland protocol error on KWin's virtual backend.
(cd "$APP/bin" && env -u DISPLAY -u WAYLAND_DISPLAY -u DBUS_SESSION_BUS_ADDRESS \
    WEBKIT_INSPECTOR_HTTP_SERVER="127.0.0.1:$port" \
    WEBKIT_DISABLE_DMABUF_RENDERER=1 \
    CALCPAD_LOG_LEVEL=verbose \
    "${envs[@]}" \
    setsid dbus-run-session -- "$HERE/session.sh" "$SOCKET" "$EXE" \
    >"$WORK/app-out.txt" 2>&1 </dev/null &)

# Ready once the inspector lists the main window.
for _ in $(seq 1 120); do
    curl -s -m 1 "http://127.0.0.1:$port/" 2>/dev/null | grep -q "/socket/" && break
    sleep 0.5
done
curl -s -m 1 "http://127.0.0.1:$port/" 2>/dev/null | grep -q "/socket/" ||
    { echo "Webview did not open inspector port $port; see $WORK/app-out.txt" >&2; exit 1; }
echo "launched: settings=${settings:-default} inspector=$port"
