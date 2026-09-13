#!/usr/bin/env bash
# Build THIRD-PARTY-NOTICES.md from the harvested corpus.
#
#   notices-preamble.md   hand-maintained: written offer, copyleft, pass-through
#   corpus/inventory.csv  machine-generated: every resolved dependency
#   corpus/*.tar.gz       the licence texts those dependencies ship
#
# The preamble is copied verbatim, then the component listing and licence texts
# are appended. Editing the generated half is pointless -- edit the preamble, or
# re-run ./harvest.sh.
#
# Reads every corpus archive to pull licence texts out, so it takes a couple of
# minutes.
#
#   ./generate-notices.sh [output]        default: ../../THIRD-PARTY-NOTICES.md

set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

readonly INVENTORY="$PWD/corpus/inventory.csv"
readonly PREAMBLE="$PWD/notices-preamble.md"
readonly OUT="${1:-$(cd ../.. && pwd)/THIRD-PARTY-NOTICES.md}"

[[ -f $INVENTORY ]] || { echo "no $INVENTORY -- run ./harvest.sh first" >&2; exit 1; }
[[ -f $PREAMBLE ]] || { echo "no $PREAMBLE" >&2; exit 1; }

cat "$PREAMBLE" >"$OUT"
python3 notices_lib.py "$INVENTORY" "$PWD/corpus" >>"$OUT"

printf 'wrote %s (%s)\n' "$OUT" "$(du -h "$OUT" | cut -f1)"
