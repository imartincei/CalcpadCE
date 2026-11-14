#!/usr/bin/env bash
# Runs the build-desktop-linux.yml job locally inside an Ubuntu 22.04 container.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$HERE/../.." && pwd)
WORK="$HERE/.work"
SRC="$WORK/src"
IMAGE=calcpad-desktop-linux-ci
DESKTOP=Calcpad.Web/frontend/calcpad-desktop
ALL_STEPS=(deps build test-x11 test-scale test-wayland arch stage verify)
TEST_STEPS=(test-x11 test-scale test-wayland)

usage() {
    cat <<EOF
Usage: tools/desktop-linux-ci/run.sh [options]

Steps (in order): ${ALL_STEPS[*]}

  --only=a,b        run only these steps
  --skip=a,b        skip these steps
  --skip-tests      skip ${TEST_STEPS[*]}
  --version=V       CALCPAD_VERSION (default: node tools/version.mjs snapshot)
  --rebuild-image   rebuild the container image without cache
  --shell           open a shell in the container instead of running steps
  --clean           delete .work (synced source, caches, artifacts) and exit
EOF
}

only="" skip="" version="" rebuild=false shell=false
for arg in "$@"; do
    case "$arg" in
        --only=*) only="${arg#*=}" ;;
        --skip=*) skip="$skip,${arg#*=}" ;;
        --skip-tests) skip="$skip,$(IFS=,; echo "${TEST_STEPS[*]}")" ;;
        --version=*) version="${arg#*=}" ;;
        --rebuild-image) rebuild=true ;;
        --shell) shell=true ;;
        --clean) rm -rf "$WORK"; echo "removed $WORK"; exit ;;
        -h|--help) usage; exit ;;
        *) echo "unknown option: $arg" >&2; usage >&2; exit 1 ;;
    esac
done

contains() { [[ ",$1," == *",$2,"* ]]; }

for s in ${only//,/ } ${skip//,/ }; do
    contains "$(IFS=,; echo "${ALL_STEPS[*]}")" "$s" || { echo "unknown step: $s" >&2; exit 1; }
done

steps=()
for s in "${ALL_STEPS[@]}"; do
    if [ -n "$only" ] && ! contains "$only" "$s"; then continue; fi
    if contains "$skip" "$s"; then continue; fi
    steps+=("$s")
done

header() { printf '\n\033[1;35m### %s\033[0m\n' "$*"; }

sync_source() {
    header "Sync working tree -> $SRC"
    mkdir -p "$SRC"
    local list="$WORK/files.txt"
    comm -23 \
        <(git -C "$REPO" -c core.quotePath=false ls-files -co --exclude-standard -- . ':!:.claude' | sort -u) \
        <(git -C "$REPO" -c core.quotePath=false ls-files -d | sort -u) > "$list.new"
    if [ -f "$list" ]; then
        comm -23 "$list" "$list.new" | while IFS= read -r f; do rm -f "$SRC/$f"; done
    fi
    rsync -a --files-from="$list.new" "$REPO/" "$SRC/"
    mv "$list.new" "$list"
}

build_image() {
    header "Build image $IMAGE"
    local sdk
    sdk=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$REPO/global.json")
    local flags=()
    if [ "$rebuild" = true ]; then flags=(--pull --no-cache); fi
    docker build "${flags[@]}" -t "$IMAGE" \
        --build-arg UID="$(id -u)" --build-arg GID="$(id -g)" --build-arg DOTNET_SDK="$sdk" \
        - < "$HERE/Dockerfile"
}

in_container() {
    local tty=()
    if [ -t 0 ] && [ -t 1 ]; then tty=(-it); fi
    mkdir -p "$WORK/home" "$WORK/cargo/registry" "$WORK/cargo/git"
    docker run --rm "${tty[@]}" --init --shm-size=1g \
        -v "$SRC:/src" \
        -v "$WORK/home:/home/ci" \
        -v "$WORK/cargo/registry:/opt/rust/cargo/registry" \
        -v "$WORK/cargo/git:/opt/rust/cargo/git" \
        -e CALCPAD_VERSION="$version" \
        "$IMAGE" "$@"
}

run_steps() {
    [ "$#" -gt 0 ] || return 0
    if ! in_container bash tools/desktop-linux-ci/steps.sh "$@"; then
        echo >&2
        echo "FAILED. Test diagnostics: $SRC/tools/desktop-test/.work/linux-tests/" >&2
        exit 1
    fi
}

build_arch_package() {
    header "Build Arch package"
    if [ "$(uname -m)" != x86_64 ]; then
        echo "skipped: CI only builds the Arch package on x86_64"
        return
    fi
    rm -f "$SRC/$DESKTOP"/packaging/arch/*.pkg.tar.zst
    docker run --rm \
        -v "$SRC:/build" \
        -e PKGDIR="/build/$DESKTOP/packaging/arch" \
        -e HOST_OWNER="$(id -u):$(id -g)" \
        archlinux:base-devel bash -c '
            set -e
            useradd -m builder
            chown -R builder "$PKGDIR"
            cd "$PKGDIR"
            sudo -u builder -H makepkg -f --nodeps
            chown -R "$HOST_OWNER" "$PKGDIR"
        '
}

if [ -z "$version" ]; then
    version=$(node "$REPO/tools/version.mjs" snapshot)
fi
echo "version: $version"

sync_source
build_image

if [ "$shell" = true ]; then
    in_container bash
    exit
fi

before=() after=() arch=false
for s in "${steps[@]}"; do
    case "$s" in
        arch) arch=true ;;
        stage|verify) after+=("$s") ;;
        *) before+=("$s") ;;
    esac
done

run_steps "${before[@]}"
if [ "$arch" = true ]; then build_arch_package; fi
run_steps "${after[@]}"

header "Done: ${steps[*]}"
if [ -d "$SRC/artifacts" ]; then echo "Artifacts: $SRC/artifacts"; fi
