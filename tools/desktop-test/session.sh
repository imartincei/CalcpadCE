#!/usr/bin/env bash
# Run by launch.sh inside a private D-Bus session: starts an off-screen KWin, then the command on
# its Wayland socket, and takes KWin down when the command exits.
#   session.sh <socket> <command...>
set -u
socket=$1; shift
kwin_wayland --virtual --socket "$socket" --width 1600 --height 1000 &
kwin=$!
trap 'kill $kwin 2>/dev/null' EXIT
for _ in $(seq 1 100); do
    [ -S "$XDG_RUNTIME_DIR/$socket" ] && break
    sleep 0.1
done
WAYLAND_DISPLAY=$socket GDK_BACKEND=wayland "$@"
