#!/usr/bin/env bash
# Build the corpus that FOSSology scans. FOSSology reads file contents, not
# manifests, so the actual dependency bytes have to be staged on disk first.
#
# Usage:  ./harvest.sh [component ...]     (default: all)

set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"
readonly REPO_ROOT="$(cd ../.. && pwd)"
readonly CORPUS="$PWD/corpus"
readonly PKGDIR="$PWD/.nuget-packages"
readonly WORK="$PWD/.work"

readonly -A COMPONENTS=(
  [calcpad-core]="csproj:Calcpad.Core/Calcpad.Core.csproj"
  [calcpad-openxml]="csproj:Calcpad.OpenXml/Calcpad.OpenXml.csproj"
  [calcpad-highlighter]="csproj:Calcpad.Highlighter/Calcpad.Highlighter.csproj"
  [calcpad-cli]="csproj:Calcpad.Cli/Calcpad.Cli.csproj"
  [calcpad-server]="csproj:Calcpad.Web/backend/Calcpad.Server.csproj"
  [pycalcpad]="csproj:Calcpad.Api/PyCalcpad/PyCalcpad.csproj"
  [calcpad-web-frontend]="npm:Calcpad.Web/frontend/calcpad-web"
  [vscode-calcpad]="npm:Calcpad.Web/frontend/vscode-calcpad"
  [calcpad-desktop]="npm:Calcpad.Web/frontend/calcpad-desktop"
  # Vendored third-party files no package manager knows about, but that ship.
  [bundled-assets]="paths:Resources/Fonts,Calcpad.Web/backend/UiAssets,Calcpad.Web/frontend/calcpad-web/public/fonts,Calcpad.Web/frontend/vscode-calcpad/fonts"
)

# Calcpad.Tests is excluded: xunit/coverlet are build-time only.

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m warn:\033[0m %s\n' "$*" >&2; }

harvest_csproj() {
  local name="$1" csproj="$REPO_ROOT/$2" dest="$WORK/$1"
  log "$name: restoring $2"
  mkdir -p "$dest"

  # Windows-only TFMs would need -p:EnableWindowsTargeting=true here.
  if ! dotnet restore "$csproj" --packages "$PKGDIR" >"$WORK/$name.restore.log" 2>&1; then
    warn "$name: restore failed, see $WORK/$name.restore.log"
    return 1
  fi

  # project.assets.json is the resolved closure, post conflict-resolution.
  local assets
  assets="$(dirname "$csproj")/obj/project.assets.json"
  [[ -f $assets ]] || { warn "$name: no project.assets.json"; return 1; }

  python3 harvest_lib.py collect-nuget "$assets" "$PKGDIR" "$dest" "$name" \
    >"$CORPUS/inventory/$name.csv"
}

harvest_paths() {
  local name="$1" dest="$WORK/$1" rel
  log "$name: staging vendored files"
  mkdir -p "$dest"

  local IFS=,
  for rel in $2; do
    local src="$REPO_ROOT/$rel"
    [[ -e $src ]] || { warn "$name: $rel does not exist"; return 1; }
    mkdir -p "$dest/$(dirname "$rel")"
    cp -r "$src" "$dest/$(dirname "$rel")/"
  done

  python3 harvest_lib.py collect-paths "$dest" "$name" >"$CORPUS/inventory/$name.csv"
}

harvest_npm() {
  local name="$1" src="$REPO_ROOT/$2" dest="$WORK/$1"
  log "$name: installing production deps for $2"
  mkdir -p "$dest"

  cp "$src/package.json" "$src/package-lock.json" "$dest/"
  if ! (cd "$dest" && npm ci --omit=dev --ignore-scripts) >"$WORK/$name.npm.log" 2>&1; then
    warn "$name: npm ci failed, see $WORK/$name.npm.log"
    return 1
  fi

  python3 harvest_lib.py collect-npm "$dest" "$name" \
    >"$CORPUS/inventory/$name.csv"
}

main() {
  command -v dotnet >/dev/null || { echo "dotnet SDK required" >&2; exit 1; }
  command -v npm >/dev/null || { echo "npm required" >&2; exit 1; }

  local -a selected=("$@")
  ((${#selected[@]})) || selected=("${!COMPONENTS[@]}")

  rm -rf "$WORK"
  mkdir -p "$CORPUS/inventory" "$WORK"

  local -a failed=()
  local name spec kind path
  for name in $(printf '%s\n' "${selected[@]}" | sort); do
    spec="${COMPONENTS[$name]:-}"
    [[ -n $spec ]] || { warn "unknown component: $name"; failed+=("$name"); continue; }
    kind="${spec%%:*}"; path="${spec#*:}"

    if ! "harvest_$kind" "$name" "$path"; then
      failed+=("$name")
      continue
    fi

    # One archive per component -> one FOSSology upload per shipped artifact.
    log "$name: packing"
    tar -czf "$CORPUS/$name.tar.gz" -C "$WORK/$name" .
  done

  # Merge every component harvested so far, so a partial run keeps the rest.
  { echo "component,ecosystem,package,version,declared_license,project_url,note"
    cat "$CORPUS"/inventory/*.csv 2>/dev/null || true
  } >"$CORPUS/inventory.csv"

  log "corpus ready: $CORPUS"
  du -sh "$CORPUS"/*.tar.gz 2>/dev/null || true
  printf '%s packages inventoried\n' "$(($(wc -l <"$CORPUS/inventory.csv") - 1))"
  if ((${#failed[@]})); then
    warn "failed components: ${failed[*]}"
    exit 1
  fi
}

main "$@"
