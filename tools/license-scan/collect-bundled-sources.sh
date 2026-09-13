#!/usr/bin/env bash
# Inventory the copyleft libraries bundled into an AppImage, and optionally
# fetch their source.
#
# Run this by hand when the bundled set changes -- not per build. Its job is to
# tell you which libraries and versions the written offer in THIRD-PARTY-NOTICES
# has to name, and to produce the source if someone actually takes you up on it.
#
# Must run on a machine matching the build (Debian/Ubuntu, same release): it
# resolves each bundled file back to the dpkg package that supplied it, so the
# versions match what shipped.
#
#   ./collect-bundled-sources.sh <AppImage> [outdir]     inventory + source
#   INVENTORY_ONLY=1 ./collect-bundled-sources.sh <AppImage>   manifest only, no downloads

set -euo pipefail

readonly APPIMAGE="${1:?usage: collect-bundled-sources.sh <AppImage> [outdir]}"
readonly OUT="$(readlink -f "${2:-bundled-sources}")"
readonly WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Deliberately broad. Over-collecting costs disk; under-collecting ships a
# binary whose source we cannot produce, so anything hinting at copyleft counts.
readonly COPYLEFT='LGPL|GNU Lesser|GPL|General Public|Mozilla Public|MPL-|CDDL|EPL-'

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m warn:\033[0m %s\n' "$*" >&2; }

command -v dpkg-query >/dev/null || { echo "dpkg required (Debian/Ubuntu only)" >&2; exit 1; }

mkdir -p "$OUT/copyright" "$OUT/source"

log "extracting $(basename "$APPIMAGE")"
( cd "$WORK" && "$(readlink -f "$APPIMAGE")" --appimage-extract >/dev/null )
readonly ROOT="$WORK/squashfs-root"

if [[ -z ${INVENTORY_ONLY:-} ]]; then
  log "enabling deb-src"
  sudo sed -i 's/^# deb-src/deb-src/' /etc/apt/sources.list
  # A silent no-op here would fail every fetch below for a non-obvious reason.
  grep -q '^deb-src' /etc/apt/sources.list \
    || { echo "no deb-src lines in /etc/apt/sources.list -- deb822 format?" >&2; exit 1; }
  sudo apt-get update -qq
fi

# basename -> package. One awk pass over dpkg's file lists; `dpkg -S` per file
# would be ~560 forks for this bundle.
log "indexing installed files"
declare -A PKG_OF=()
while IFS=$'\t' read -r base pkg; do
  [[ -n $base && -z ${PKG_OF[$base]:-} ]] && PKG_OF[$base]="$pkg"
done < <(awk '
  FNR==1 { n=split(FILENAME,a,"/"); p=a[n]; sub(/\.list$/,"",p); sub(/:.*/,"",p) }
  { b=$0; sub(/.*\//,"",b); if (b != "") print b "\t" p }
' /var/lib/dpkg/info/*.list)
log "indexed ${#PKG_OF[@]} filenames"

declare -A SEEN_SRC=()
declare -a MISSING=()
manifest="$OUT/manifest.csv"
echo "bundled_file,binary_package,source_package,version,copyleft,source_status" >"$manifest"

log "resolving bundled objects to packages"
while IFS= read -r f; do
  base="$(basename "$f")"
  pkg="${PKG_OF[$base]:-}"
  # No owning package means it is ours (the app, the .NET runtime, Skia).
  [[ -n $pkg ]] || continue

  src="$(dpkg-query -W -f='${source:Package}' "$pkg" 2>/dev/null || true)"
  ver="$(dpkg-query -W -f='${source:Version}' "$pkg" 2>/dev/null || true)"
  [[ -n $src && -n $ver ]] || { warn "no source info for $pkg"; continue; }

  cp_file="/usr/share/doc/$pkg/copyright"
  copyleft=no
  if [[ -f $cp_file ]]; then
    cp -n "$cp_file" "$OUT/copyright/$pkg.copyright" 2>/dev/null || true
    grep -qiE "$COPYLEFT" "$cp_file" && copyleft=yes
  else
    # Unknown licence is treated as copyleft: fetch it and let review decide.
    copyleft=unknown
  fi

  status=skipped
  if [[ $copyleft != no && -z ${INVENTORY_ONLY:-} ]]; then
    key="$src=$ver"
    if [[ -v SEEN_SRC[$key] ]]; then
      status="${SEEN_SRC[$key]}"
    else
      if ( cd "$OUT/source" && apt-get source --download-only -qq "$src=$ver" ) 2>/dev/null; then
        status=ok
      else
        status=FAILED
        MISSING+=("$src=$ver (for $base)")
      fi
      SEEN_SRC[$key]="$status"
    fi
  fi
  printf '%s,%s,%s,%s,%s,%s\n' "${f#$ROOT}" "$pkg" "$src" "$ver" "$copyleft" "$status" >>"$manifest"
done < <(find "$ROOT" -type f \( -name '*.so' -o -name '*.so.*' -o -perm -u+x \) | sort)

if ((${#MISSING[@]})); then
  printf '%s\n' "${MISSING[@]}" >"$OUT/MISSING-SOURCES.txt"
  warn "could not fetch source for ${#MISSING[@]} package(s) -- see $OUT/MISSING-SOURCES.txt"
  warn "each one is a library shipped without corresponding source; resolve before release"
fi

log "collected $(find "$OUT/source" -maxdepth 1 -type f | wc -l) source files into $OUT"
du -sh "$OUT"
