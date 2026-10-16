#!/usr/bin/env bash
# Build THIRD-PARTY-NOTICES.txt from the harvested corpus.
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
#   ./generate-notices.sh [output]        default: ../../THIRD-PARTY-NOTICES.txt

set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

readonly INVENTORY="$PWD/corpus/inventory.csv"
readonly PREAMBLE="$PWD/notices-preamble.md"
readonly OUT="${1:-$(cd ../.. && pwd)/THIRD-PARTY-NOTICES.txt}"

[[ -f $INVENTORY ]] || { echo "no $INVENTORY -- run ./harvest.sh first" >&2; exit 1; }
[[ -f $PREAMBLE ]] || { echo "no $PREAMBLE" >&2; exit 1; }

cat "$PREAMBLE" >"$OUT"
python3 notices_lib.py "$INVENTORY" "$PWD/corpus" >>"$OUT"

printf 'wrote %s (%s)\n' "$OUT" "$(du -h "$OUT" | cut -f1)"

# An open marker means an obligation is described but not discharged. Shipping
# that is worse than shipping nothing, so fail rather than let it through.
fail=0
if grep -n 'TODO' "$OUT"; then
  echo "^^ unresolved TODO -- discharge it or drop the claim" >&2
  fail=1
fi
if grep -n '^## Held back pending verification' "$OUT"; then
  echo "^^ components held back -- confirm whether each ships, then list or drop it" >&2
  fail=1
fi
exit "$fail"
