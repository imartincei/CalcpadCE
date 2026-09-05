#!/usr/bin/env bash
# Snapshot the FOSSology state that cannot be regenerated.
#
# The corpus and scans are reproducible; the human clearing decisions are not.
# They exist only in Postgres.
#
#   ./backup.sh              live snapshot, safe to run mid-scan
#   ./backup.sh --quiesce    stop agents first for a strictly consistent one
#
# Restore with ./restore.sh backups/<timestamp>

set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

readonly STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
readonly DEST="$PWD/backups/$STAMP"

quiesce=0
[[ ${1:-} == --quiesce ]] && quiesce=1

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

docker compose ps --status running --quiet db | grep -q . || {
  echo "db container is not running; start it with 'docker compose up -d'" >&2
  exit 1
}

mkdir -p "$DEST"

if ((quiesce)); then
  log "stopping agents for a consistent snapshot"
  docker compose stop scheduler web
  restart_agents() { log "restarting agents"; docker compose start scheduler web; }
  trap restart_agents EXIT
fi

# Dump the DB before archiving the repository: a row only exists after its
# file is written, so a later archive is a superset. Dangling rows would not be.
log "dumping database"
docker compose exec -T db \
  pg_dump --username=fossy --format=custom --compress=9 fossology \
  >"$DEST/fossology.dump"

log "archiving repository volume"
# --volumes-from avoids guessing the generated volume name, and works on a
# stopped container (so --quiesce is fine).
docker run --rm \
  --volumes-from "$(docker compose ps --all --quiet scheduler)" \
  -v "$DEST":/backup \
  alpine:3 tar -czf /backup/repository.tar.gz -C /srv/fossology/repository .

{
  echo "created: $STAMP"
  echo "quiesced: $((quiesce))"
  echo "images:"
  docker compose config --images | sed 's/^/  /'
} >"$DEST/manifest.txt"

log "snapshot written to $DEST"
du -sh "$DEST"
