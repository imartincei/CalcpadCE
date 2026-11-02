#!/bin/sh
set -eu

# One-shot, idempotent Garage bootstrap: assign the layout, create an access key that may
# create buckets, and write its credentials for the s3-server. Buckets are created later
# through the s3-server API, which gives the key owner rights on each one.

GARAGE_BIN="/garage"
KEY_NAME="${CALCPAD_KEY_NAME:-calcpad-s3-server}"
ZONE="${CALCPAD_ZONE:-dc1}"
CAPACITY="${CALCPAD_CAPACITY:-10G}"

echo "[garage-init] waiting for garage RPC..."
for i in $(seq 1 60); do
    if "$GARAGE_BIN" status >/dev/null 2>&1; then break; fi
    sleep 1
done

NODE_ID="$("$GARAGE_BIN" node id -q 2>/dev/null | cut -d@ -f1 || true)"
if [ -n "$NODE_ID" ]; then
    if ! "$GARAGE_BIN" layout show 2>/dev/null | grep -q "$NODE_ID"; then
        echo "[garage-init] assigning layout for node $NODE_ID"
        "$GARAGE_BIN" layout assign -z "$ZONE" -c "$CAPACITY" "$NODE_ID"
        "$GARAGE_BIN" layout apply --version 1
    fi
fi

if ! "$GARAGE_BIN" key info "$KEY_NAME" >/dev/null 2>&1; then
    echo "[garage-init] creating key $KEY_NAME"
    "$GARAGE_BIN" key create "$KEY_NAME"
fi
"$GARAGE_BIN" key allow --create-bucket "$KEY_NAME"

CREDS_FILE="${CALCPAD_CREDS_FILE:-/shared/garage-creds.env}"
mkdir -p "$(dirname "$CREDS_FILE")"
INFO="$("$GARAGE_BIN" key info --show-secret "$KEY_NAME")"
ACCESS="$(echo "$INFO" | awk '/Key ID:/ {print $3}')"
SECRET="$(echo "$INFO" | awk '/Secret key:/ {print $3}')"

cat >"$CREDS_FILE" <<EOF
S3__ServiceURL=http://garage:3900
S3__Region=garage
S3__AccessKey=$ACCESS
S3__SecretKey=$SECRET
S3__ForcePathStyle=true
EOF

echo "[garage-init] wrote credentials to $CREDS_FILE"
