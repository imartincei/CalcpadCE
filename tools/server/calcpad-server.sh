#!/usr/bin/env bash
# Lint or render CalcpadCE files with the headless Calcpad.Server. Needs bash, curl, sed and awk.
#   tools/server/calcpad-server.sh lint [--info] <file.cpd> ...
#   tools/server/calcpad-server.sh render <file.cpd> <out.txt>
# CALCPAD_SERVER picks the server binary; CALCPAD_URL reuses a running server.
set -euo pipefail

usage() { sed -n '2,5s/^# \{0,1\}//p' "$0" >&2; exit 2; }
[ $# -ge 2 ] || usage
command=$1; shift
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
tmp=$(mktemp -d)
server_pid=
cleanup() { [ -n "$server_pid" ] && kill "$server_pid" 2>/dev/null; rm -rf "$tmp"; }
trap cleanup EXIT

case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) windows=1; exe=Calcpad.Server.exe ;; *) windows=; exe=Calcpad.Server ;; esac
case "$(uname -m)" in aarch64|arm64) arch=arm64 ;; *) arch=x64 ;; esac
rid=$([ -n "$windows" ] && echo "win-$arch" || echo "linux-$arch")

native_path() { if [ -n "$windows" ]; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
abs_path() { (cd "$(dirname "$1")" && printf '%s/%s' "$(pwd)" "$(basename "$1")"); }

start_server() {
    if [ -n "${CALCPAD_URL:-}" ]; then url=${CALCPAD_URL%/}; return; fi
    local server=${CALCPAD_SERVER:-}
    if [ -z "$server" ]; then
        server=$(ls -t "$root"/Calcpad.Web/backend/bin/{Release,Debug}/net10.0/{,"$rid"/}"$exe" 2>/dev/null | head -1 || true)
    fi
    [ -x "$server" ] || { echo "Calcpad.Server not found. Build Calcpad.Web/backend, or set CALCPAD_SERVER." >&2; exit 2; }
    server=$(abs_path "$server")
    local parent=$$
    [ -r /proc/$$/winpid ] && parent=$(cat /proc/$$/winpid)
    local port_file="$tmp/port"
    (cd "$(dirname "$server")" && exec "$server" --port-file "$(native_path "$port_file")" \
        --parent-pid "$parent" --no-exit-on-stdin-close >/dev/null 2>&1 </dev/null) &
    server_pid=$!
    for _ in $(seq 600); do
        [ -s "$port_file" ] && break
        kill -0 "$server_pid" 2>/dev/null || { echo "Calcpad.Server exited before it started." >&2; exit 2; }
        sleep 0.1
    done
    [ -s "$port_file" ] || { echo "Timed out waiting for Calcpad.Server." >&2; exit 2; }
    url=$(head -1 "$port_file" | tr -d '\r')
    for _ in $(seq 300); do
        curl -sf "$url/api/calcpad/health" >/dev/null && return
        sleep 0.1
    done
    echo "Calcpad.Server at $url never became healthy." >&2; exit 2
}

# Writes the request body for a file: its text and absolute path, JSON-escaped.
request_body() {
    local file=$1 extra=$2 path
    path=$(native_path "$(abs_path "$file")" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g')
    {
        printf '{"sourceFilePath":"%s",%s"content":"' "$path" "$extra"
        sed -e 's/\r$//' -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/\t/\\t/g' "$file" | awk '{ printf "%s\\n", $0 }'
        printf '"}'
    } > "$tmp/body.json"
}

post() {
    curl -sS --fail-with-body -X POST -H 'Content-Type: application/json; charset=utf-8' \
        ${CALCPAD_API_TOKEN:+-H "X-Calcpad-Token: $CALCPAD_API_TOKEN"} \
        --data-binary @"$tmp/body.json" "$url$1" "${@:2}"
}

lint() {
    local errors=0 info= file
    if [ "${1:-}" = --info ]; then info=1; shift; fi
    [ $# -ge 1 ] || usage
    for file in "$@"; do
        request_body "$file" ''
        post /api/calcpad/lint -o "$tmp/lint.json"
        # One diagnostic per line, then "line column severity code message".
        sed -e 's/{"line":/\n{"line":/g' "$tmp/lint.json" \
            | sed -nE 's/^\{"line":([0-9]+),"column":([0-9]+),"endColumn":-?[0-9]+,"code":"([^"]*)","message":"((\\.|[^"\\])*)","severity":"([^"]*)".*/\1 \2 \6 \3 \4/p' \
            > "$tmp/found"
        : > "$tmp/rows"
        while read -r line column severity code message || [ -n "$line" ]; do
            severity=$(printf '%s' "$severity" | tr '[:upper:]' '[:lower:]')
            [ "$severity" = information ] && [ -z "$info" ] && continue
            printf '%s\t  %s:%s [linter] %s %s %s\n' $((line + 1)) $((line + 1)) $((column + 1)) "$severity" "$code" "$message" >> "$tmp/rows"
        done < "$tmp/found"
        # Runtime errors come from running the worksheet; their lines are already 1-based.
        request_body "$file" '"includeLineAnchors":true,"write":false,'
        post /api/calcpad/convert -o /dev/null -D "$tmp/headers"
        raw=$(sed -n 's/^[Xx]-[Cc]alcpad-[Ee]rrors: *//p' "$tmp/headers" | tr -d '\r')
        printf '%b' "${raw//%/\\x}" | sed -e 's/{"sourceLine":/\n{"sourceLine":/g' \
            | sed -nE 's/^\{"sourceLine":([0-9]+),"outputLine":-?[0-9]+,"message":"((\\.|[^"\\])*)","source":"[^"]*","code":"?([^",]*)"?.*/\1 \4 \2/p' \
            | while read -r line code message || [ -n "$line" ]; do
                grep -q "^$line"$'\t'".* \\[linter\\] error $code " "$tmp/rows" && continue
                printf '%s\t  %s [runtime] error %s %s\n' "$line" "$line" "$code" "$message"
            done >> "$tmp/rows"
        echo "$file"
        sort -n -s -k1,1 "$tmp/rows" | cut -f2- \
            | sed -e 's/\\"/"/g' -e 's/\\u0022/"/g' -e 's/\\u002B/+/g' -e "s/\\\\u0027/'/g" \
                -e 's/\\u003C/</g' -e 's/\\u003E/>/g' -e 's/\\u0026/\&/g'
        errors=$((errors + $(grep -c '] error ' "$tmp/rows" || true)))
    done
    echo "$errors error(s)"
    [ "$errors" -eq 0 ]
}

render() {
    [ $# -eq 2 ] || usage
    request_body "$1" '"forPrint":true,"includeLineAnchors":true,"write":false,'
    post /api/calcpad/convert -o "$tmp/report.html"
    # Drops <script> and <style> blocks, then the remaining tags.
    awk '{
        out = ""; line = $0
        while (line != "") {
            low = tolower(line)
            if (skip != "") {
                i = index(low, "</" skip)
                if (!i) { line = ""; break }
                line = substr(line, i); line = substr(line, index(line, ">") + 1); skip = ""
                continue
            }
            s = index(low, "<script"); t = index(low, "<style")
            if (!s && !t) { out = out line; break }
            if (!s || (t && t < s)) { s = t; skip = "style" } else skip = "script"
            out = out substr(line, 1, s - 1); line = substr(line, s + 1)
        }
        print out
    }' "$tmp/report.html" \
        | sed -E -e 's#</t[dh][^>]*>#   #Ig' -e 's#<br[^>]*>|</(p|div|tr|h[1-6]|li)[^>]*>#\n#Ig' -e 's/<[^>]*>//g' \
            -e 's/&nbsp;|&#160;/ /g' -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&quot;/"/g' -e "s/&#39;/'/g" -e 's/&amp;/\&/g' \
        | sed -E -e 's/[[:space:]]+/ /g' -e 's/^ //' -e 's/ $//' -e '/^$/d' > "$2"
    grep -E '^Error in ' "$2" || true
    echo "$2 ($(grep -c 'Error in ' "$2" || true) calculation error(s))"
}

case "$command" in
    lint) start_server; lint "$@" ;;
    render) start_server; render "$@" ;;
    *) usage ;;
esac
