#!/usr/bin/env bash
set -euo pipefail

INTERVAL=10
MD="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/CLAUDE.md"

session=$(jq -r '.session_id')
state="${TMPDIR:-/tmp}/claude-md-reminder-${session//[^A-Za-z0-9_-]/_}"

n=$(( $(cat "$state" 2>/dev/null || echo 0) + 1 ))
printf '%s' "$n" > "$state"
(( n == 1 || n % INTERVAL == 0 )) || exit 0

jq -n --arg md "$(cat "$MD")" '{
  hookSpecificOutput: {
    hookEventName: "PostToolUse",
    additionalContext: "IMPORTANT reminder - project instructions from CLAUDE.md. You MUST follow them exactly:\n\n\($md)"
  },
  suppressOutput: true
}'
