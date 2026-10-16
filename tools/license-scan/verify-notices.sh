#!/usr/bin/env bash
# Assert every distributed artifact carries THIRD-PARTY-NOTICES.txt.
#
# The 8.0.0-beta1 desktop .deb shipped 385 files and zero attribution, so this
# is a real regression guard, not a formality. Run it on each built artifact
# before it is uploaded.
#
#   ./verify-notices.sh <artifact> [artifact ...]
#
# Accepts .deb, .rpm, .pkg.tar.zst, .zip, .vsix, .tar.* or a directory.

set -uo pipefail

readonly NOTICES="THIRD-PARTY-NOTICES.txt"
# A truncated or stale-empty generate would pass a bare presence check.
readonly MIN_BYTES=200000

fail=0
note() { printf '  %s\n' "$*"; }

have() { command -v "$1" >/dev/null 2>&1; }

# A .deb is an ar archive; its payload member may be .gz, .xz or .zst, so let
# whichever extractor is present sniff the compression rather than assuming.
deb_listing() {
  have dpkg-deb && { dpkg-deb -c "$1"; return; }
  local member
  member=$(ar t "$1" 2>/dev/null | grep '^data\.tar' | head -1)
  [[ -n $member ]] || return 1
  if have bsdtar; then ar p "$1" "$member" | bsdtar -tf -
  else ar p "$1" "$member" | tar -taf -
  fi
}

listing() {
  case "$1" in
    *.deb)          deb_listing "$1" ;;
    *.rpm)          have rpm && rpm -qlp "$1" \
                      || { have bsdtar && bsdtar -tf "$1"; } \
                      || { have rpm2cpio && rpm2cpio "$1" | cpio -t 2>/dev/null; } ;;
    *.zip|*.vsix)   unzip -l "$1" ;;
    *.tar.*|*.tgz)  have bsdtar && bsdtar -tf "$1" || tar -taf "$1" ;;
    *)              [[ -d $1 ]] && find "$1" -type f ;;
  esac
}

# The repo copy is the one that gets packaged, so size-check it once.
root="$(cd "$(dirname "$(readlink -f "$0")")/../.." && pwd)"
if [[ -f "$root/$NOTICES" ]]; then
  size=$(wc -c <"$root/$NOTICES")
  if ((size < MIN_BYTES)); then
    note "FAIL $NOTICES is only ${size}B (< ${MIN_BYTES}B) -- truncated or not generated"
    fail=1
  fi
  if grep -q 'TODO' "$root/$NOTICES"; then
    note "FAIL $NOTICES still contains TODO -- an obligation is undischarged"
    fail=1
  fi
fi

for artifact in "$@"; do
  if [[ ! -e $artifact ]]; then
    note "FAIL $artifact does not exist"
    fail=1
    continue
  fi

  out=$(listing "$artifact")
  if [[ -z $out ]]; then
    note "FAIL $artifact -- could not read archive listing"
    fail=1
  elif grep -qF "$NOTICES" <<<"$out"; then
    note "ok   $(basename "$artifact")"
  else
    note "FAIL $(basename "$artifact") does not contain $NOTICES"
    fail=1
  fi
done

((fail)) && echo "third-party notices missing from a distributed artifact" >&2
exit "$fail"
