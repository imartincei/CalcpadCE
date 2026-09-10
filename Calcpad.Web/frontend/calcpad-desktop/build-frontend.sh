#!/usr/bin/env bash
# Rebuild the calcpad-web frontend (and its calcpad-frontend dependency) into
# calcpad-web/dist, which Tauri embeds into the app binary at compile time.
#
# Invoked from tauri.linux.conf.json's beforeBuildCommand. The dev-mode watch
# variant lives in dev-frontend.sh, which also owns the watcher lifecycle.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Unlike the dev script, wipe dist first: a release bundle must not inherit
# assets from an earlier build.
rm -rf ../calcpad-web/dist

./local-npm.sh --prefix ../calcpad-frontend run build
./local-npm.sh --prefix ../calcpad-web run build
