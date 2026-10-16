#!/usr/bin/env bash
# beforeDevCommand for `tauri dev`: build the frontend, start the rebuild
# watchers, and don't return until calcpad-web/dist is complete.
#
# The watchers were previously backgrounded inline with `(... &)`, which tauri
# never owned — they outlived every `tauri dev` and piled up, all emptying and
# rewriting dist at once. That presents as a frontend that loads broken assets
# (window titles stuck at the bare configured "CalcpadCE"), `cargo` failing with
# `Text file busy`, or vite dying on `ENOTEMPTY .../dist/assets`. Their process
# groups are recorded so the next run kills them first; `--stop` does it by hand.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PID_FILE=/tmp/calcpad-dev-watchers.pids
FRONTEND_LOG=/tmp/calcpad-frontend-watch.log
WEB_LOG=/tmp/calcpad-web-watch.log
DIST=../calcpad-web/dist
BUILD_TIMEOUT_S=180

# PIDs get recycled, so only kill a group that still looks like one of ours. The
# npm wrapper exits once node is up, so the group leader is normally gone already —
# identify the group by any live member, not by /proc/<pgid>.
kill_group() {
    local pgid=$1
    [ -n "$pgid" ] || return 0
    if ps -eo pgid=,args= | awk -v g="$pgid" '$1 == g' | grep -qE 'calcpad-frontend|calcpad-web'; then
        kill -- "-$pgid" 2>/dev/null || true
    fi
}

stop_watchers() {
    [ -f "$PID_FILE" ] || return 0
    while read -r pgid; do kill_group "$pgid"; done < "$PID_FILE"
    rm -f "$PID_FILE"
}

if [ "${1:-}" = "--stop" ]; then
    stop_watchers
    echo "dev-frontend: watchers stopped"
    exit 0
fi

group_alive() {
    ps -eo pgid= | tr -d ' ' | grep -qx "$1"
}

# setsid puts each watcher in its own group so killing it takes the npm -> node
# children with it. Read the group back rather than assuming $! leads it; the
# caller gets it in LAST_PGID.
start_watcher() {
    local log=$1 prefix=$2 script=$3 pid pgid
    setsid ./local-npm.sh --prefix "$prefix" run "$script" > "$log" 2>&1 &
    pid=$!
    sleep 0.2
    pgid=$(ps -o pgid= -p "$pid" 2>/dev/null | tr -d ' ' || true)
    LAST_PGID=${pgid:-$pid}
    echo "$LAST_PGID" >> "$PID_FILE"
}

# Fails fast on a build error, or on the watcher dying (a bad npm script, a node
# crash), instead of sitting out the timeout.
wait_for_build() {
    local log=$1 label=$2 pgid=$3 waited=0
    while ! grep -qa 'built in' "$log" 2>/dev/null; do
        if grep -qa 'error during build\|ENOTEMPTY' "$log" 2>/dev/null || ! group_alive "$pgid"; then
            echo "dev-frontend: $label failed" >&2
            tail -25 "$log" >&2 || true
            exit 1
        fi
        sleep 0.5
        waited=$((waited + 1))
        if [ "$waited" -gt $((BUILD_TIMEOUT_S * 2)) ]; then
            echo "dev-frontend: $label did not finish in ${BUILD_TIMEOUT_S}s; see $log" >&2
            tail -25 "$log" >&2 || true
            exit 1
        fi
    done
}

stop_watchers

# calcpad-web compiles against calcpad-frontend's emitted output, so that lands
# first — and `run build` also runs the postbuild codegen that `tsc --watch` never does.
./local-npm.sh --prefix ../calcpad-frontend run build

: > "$FRONTEND_LOG"
: > "$WEB_LOG"
start_watcher "$FRONTEND_LOG" ../calcpad-frontend watch
start_watcher "$WEB_LOG" ../calcpad-web build:watch
web_pgid=$LAST_PGID

# No separate `calcpad-web run build`: the watcher's own initial build is the one
# build needed, and waiting for it is what keeps the webview off a half-written dist.
wait_for_build "$WEB_LOG" "calcpad-web build" "$web_pgid"

# "built in" can precede the files settling, so confirm the entry the page loads.
entry=""
for _ in $(seq 40); do
    entry=$(grep -oE 'assets/index-[A-Za-z0-9_-]+\.js' "$DIST/index.html" 2>/dev/null | head -1 || true)
    [ -n "$entry" ] && [ -f "$DIST/$entry" ] && break
    sleep 0.25
done
if [ -z "$entry" ] || [ ! -f "$DIST/$entry" ]; then
    echo "dev-frontend: $DIST is incomplete after the build" >&2
    exit 1
fi

echo "dev-frontend: dist ready ($entry); watcher groups $(tr '\n' ' ' < "$PID_FILE")"
