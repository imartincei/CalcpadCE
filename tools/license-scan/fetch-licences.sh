#!/usr/bin/env bash
# Fetch licence texts for components that ship none, from their own repository.
# Writes corpus/licences-fetched/ ; re-run after harvest.sh when deps change.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
exec python3 fetch_licences.py "${1:-corpus/inventory.csv}" "${2:-corpus}"
