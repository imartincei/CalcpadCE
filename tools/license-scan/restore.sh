#!/usr/bin/env bash
# Restore a snapshot taken by backup.sh.
#
#   ./restore.sh backups/20260905T190000Z
#
# DESTROYS the current database and repository.

set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

SRC="${1:?usage: ./restore.sh backups/<timestamp>}"
readonly SRC="$(readlink -f "$SRC")"
[[ -f $SRC/fossology.dump ]] || { echo "no fossology.dump in $SRC" >&2; exit 1; }
[[ -f $SRC/repository.tar.gz ]] || { echo "no repository.tar.gz in $SRC" >&2; exit 1; }

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

cat "$SRC/manifest.txt" 2>/dev/null || true
read -rp "This will erase the current FOSSology state. Continue? [y/N] " reply
[[ $reply == [yY] ]] || { echo "aborted"; exit 1; }

log "stopping stack and discarding current volumes"
docker compose down --volumes

log "starting database only"
docker compose up -d db
until docker compose exec -T db pg_isready -U fossy -d fossology >/dev/null 2>&1; do
  sleep 1
done

log "restoring database"
# --clean --if-exists also handles a partially initialised DB.
docker compose exec -T db \
  pg_restore --username=fossy --dbname=fossology --clean --if-exists --no-owner \
  <"$SRC/fossology.dump"

# `create` makes the volume without running the entrypoint, so nothing is
# writing to it while we untar.
log "materialising the repository volume"
docker compose create scheduler

log "restoring repository"
docker run --rm \
  --volumes-from "$(docker compose ps --all --quiet scheduler)" \
  -v "$SRC":/backup:ro \
  alpine:3 sh -c 'rm -rf /srv/fossology/repository/* &&
                  tar -xzf /backup/repository.tar.gz -C /srv/fossology/repository'

log "bringing the stack up"
docker compose up -d

log "restored from $SRC -- check http://localhost:8081/repo/"
