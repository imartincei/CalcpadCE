#!/usr/bin/env bash
# Upload a harvested component to FOSSology, run the scanners, fetch a report.
#
#   ./scan.sh calcpad-core                    upload + scan
#   ./scan.sh calcpad-core --report readmeoss fetch the notices report too
#   ./scan.sh --report readmeoss --only 2     just re-fetch a report for upload 2
#
# Reports land in ./reports/. Formats: readmeoss (third-party notices),
# spdx2 / spdx2tv / spdx3json (SPDX), cyclonedx, unifiedreport (xlsx), clixml.
#
# NOTE: readmeoss only emits *concluded* licenses. Undecided scanner findings
# are silently absent -- see README.

set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

readonly API="http://localhost:8081/repo/api/v1"
readonly TOKEN_CACHE="$PWD/.token"
readonly FOLDER_ID=1

component=""
report_format=""
only_upload=""

while (($#)); do
  case "$1" in
    --report) report_format="${2:?--report needs a format}"; shift 2 ;;
    --only) only_upload="${2:?--only needs an upload id}"; shift 2 ;;
    -*) echo "unknown flag: $1" >&2; exit 1 ;;
    *) component="$1"; shift ;;
  esac
done

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }

api() { curl -sS -H "Authorization: $TOKEN" "$@"; }

get_token() {
  # Tokens max out at 30 days.
  if [[ -f $TOKEN_CACHE ]]; then
    TOKEN="$(cat "$TOKEN_CACHE")"
    if curl -sS -o /dev/null -f -H "Authorization: $TOKEN" "$API/folders" 2>/dev/null; then
      return
    fi
    log "cached token rejected, minting a new one"
  fi
  local expire response
  expire="$(date -u -d '+29 days' +%Y-%m-%d)"
  response="$(curl -sS -X POST "$API/tokens" -H 'Content-Type: application/json' \
    -d "{\"username\":\"${FOSSOLOGY_USER:-fossy}\",\"password\":\"${FOSSOLOGY_PASSWORD:-fossy}\",\"token_name\":\"scan-$(date +%s)\",\"token_scope\":\"write\",\"token_expire\":\"$expire\"}")"
  TOKEN="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("Authorization",""))' <<<"$response")"
  [[ -n $TOKEN ]] || die "could not get a token: $response"
  umask 077; printf '%s' "$TOKEN" >"$TOKEN_CACHE"
}

wait_for_jobs() {
  local pending
  while :; do
    pending="$(docker compose exec -T db psql -U fossy -d fossology -tAc \
      'select count(*) from jobqueue where jq_endtime is null;' | tr -d '[:space:]')"
    [[ $pending == 0 ]] && break
    printf '\r    %s job(s) running...' "$pending"
    sleep 10
  done
  printf '\r'
  # jq_end_bits != 1 means the agent failed, which otherwise just looks
  # like an upload with no findings.
  local failed
  failed="$(docker compose exec -T db psql -U fossy -d fossology -tAc \
    "select string_agg(jq_type, ', ') from jobqueue where jq_end_bits <> 1;" | tr -d '\n')"
  [[ -z ${failed// /} ]] || die "agents failed: $failed"
}

upload_component() {
  local name="$1" archive="/tmp/corpus/$1.tar.gz"
  [[ -f "corpus/$name.tar.gz" ]] || die "corpus/$name.tar.gz not found -- run ./harvest.sh $name"

  log "$name: uploading from server"
  # This API takes its params as headers and wants the path wrapped in a
  # "location" object. /tmp is the UploadFromServerWhitelist default.
  local response
  response="$(api -X POST "$API/uploads" -H 'Content-Type: application/json' \
    -H "folderId: $FOLDER_ID" -H "uploadDescription: $name corpus" -H 'uploadType: server' \
    -d "{\"location\":{\"path\":\"$archive\",\"name\":\"$name\"}}")"
  grep -q '"code":201' <<<"$response" || die "upload failed: $response"

  log "$name: unpacking and indexing"
  wait_for_jobs

  UPLOAD_ID="$(docker compose exec -T db psql -U fossy -d fossology -tAc \
    "select upload_pk from upload where upload_filename = '$name' order by upload_pk desc limit 1;" \
    | tr -d '[:space:]')"
  [[ -n $UPLOAD_ID ]] || die "could not resolve upload id for $name"
  log "$name: upload id $UPLOAD_ID"
}

scan_upload() {
  log "scanning upload $UPLOAD_ID (nomos, monk, ojo, copyright, pkgagent)"
  # decider auto-concludes unambiguous cases; the rest need the Browse UI.
  local response
  response="$(api -X POST "$API/jobs" -H 'Content-Type: application/json' \
    -H "folderId: $FOLDER_ID" -H "uploadId: $UPLOAD_ID" \
    -d '{"analysis":{"bucket":false,"copyright_email_author":true,"ecc":false,
         "keyword":false,"monk":true,"mimetype":false,"package":true,"reso":false,
         "heritage":false,"nomos":true,"ojo":true},
         "decider":{"nomos_monk":true,"bulk_reused":false,"new_scanner":false,
         "ojo_decider":true}}')"
  grep -q '"code":201' <<<"$response" || die "scan scheduling failed: $response"
  wait_for_jobs
  log "scan complete"
}

fetch_report() {
  local fmt="$1" link response code
  log "generating $fmt report for upload $UPLOAD_ID"
  response="$(api "$API/report" -H "uploadId: $UPLOAD_ID" -H "reportFormat: $fmt")"
  link="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("message",""))' <<<"$response")"
  [[ $link == http* ]] || die "report request failed: $response"

  mkdir -p reports
  local out="reports/${COMPONENT_NAME:-upload$UPLOAD_ID}.$fmt.txt"
  for _ in $(seq 1 60); do
    code="$(api -o "$out" -w '%{http_code}' "$link")"
    [[ $code == 200 ]] && { log "report written to $out"; return; }
    sleep 5
  done
  die "report did not become ready (last HTTP $code)"
}

docker compose ps --status running --quiet web | grep -q . \
  || die "FOSSology is not running -- 'docker compose up -d' first"
get_token

if [[ -n $only_upload ]]; then
  UPLOAD_ID="$only_upload"
else
  [[ -n $component ]] || die "usage: ./scan.sh <component> [--report <format>]"
  COMPONENT_NAME="$component"
  upload_component "$component"
  scan_upload
fi

[[ -n $report_format ]] && fetch_report "$report_format"
exit 0
