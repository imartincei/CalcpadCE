#!/bin/bash
# Restart Calcpad Development Server
# Kills any existing server on port 9420 and starts a new one

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/.."
PORT=9420

# Kill any existing process on the port
PID=$(lsof -t -i:$PORT 2>/dev/null)
if [ -n "$PID" ]; then
    echo "Killing existing server (PID: $PID)..."
    kill $PID 2>/dev/null
    sleep 1
fi

# Load environment from .env file if present
ENV_FILE="$SERVER_DIR/.env"
if [ -f "$ENV_FILE" ]; then
    while IFS='=' read -r key value; do
        # Skip comments and blank lines, strip carriage returns
        key=$(echo "$key" | tr -d '\r')
        value=$(echo "$value" | tr -d '\r')
        [[ -z "$key" || "$key" == \#* ]] && continue
        export "$key=$value"
    done < "$ENV_FILE"
fi

# Build and run the server
cd "$SERVER_DIR"

echo "Building Calcpad.Server..."
dotnet build Calcpad.Server.csproj -c Debug -v q

if [ $? -eq 0 ]; then
    echo "Starting server on port $PORT..."
    # Without CALCPAD_PORT the server takes a random free port, which the kill above can't find next time.
    CALCPAD_PORT=$PORT ASPNETCORE_ENVIRONMENT=Development dotnet run --project Calcpad.Server.csproj --no-build --no-launch-profile &
    sleep 2
    echo "Server started. PID: $!"
else
    echo "Build failed!"
    exit 1
fi
