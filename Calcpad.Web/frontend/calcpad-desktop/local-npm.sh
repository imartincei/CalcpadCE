#!/usr/bin/env bash
# Wrapper that runs npm using the project-local Node 24 (Active LTS) in .node/,
# so we don't inherit whatever Node version happens to be on the user's PATH.
# Used by tauri.conf.json's beforeDevCommand / beforeBuildCommand.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export PATH="$SCRIPT_DIR/.node/bin:$PATH"
exec npm "$@"
