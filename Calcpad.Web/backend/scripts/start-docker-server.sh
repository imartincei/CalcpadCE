#!/bin/bash

# CalcpadServer Docker Startup Script
# This script starts the CalcpadServer API as a Docker container in the background

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_NAME="calcpad-server"

echo "🚀 Starting CalcpadServer..."

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed. Please install Docker first."
    exit 1
fi

# Check if Docker Compose is available
if ! command -v docker &> /dev/null || ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose is not available. Please install Docker Compose."
    exit 1
fi

cd "$SCRIPT_DIR"

# Stop existing container if running
echo "🛑 Stopping existing containers..."
docker compose down --remove-orphans 2>/dev/null || true

# Remove old images if requested
if [[ "$1" == "--rebuild" ]]; then
    echo "🔨 Rebuilding images..."
    docker compose build --no-cache
else
    echo "🏗️  Building images..."
    docker compose build
fi

# Start the containers in detached mode
echo "🌟 Starting CalcpadServer containers..."
docker compose up -d

# Wait for the service to be ready
echo "⏳ Waiting for service to be ready..."
sleep 10

# Check if the service is running
if curl -f -s http://localhost:9420/api/calcpad/sample > /dev/null; then
    echo "✅ CalcpadServer is running successfully!"
    echo ""
    echo "📡 API Endpoints:"
    echo "   - Health Check: http://localhost:9420/api/calcpad/sample"
    echo "   - Convert API: http://localhost:9420/api/calcpad/convert"
    echo "   - Swagger UI: http://localhost:9420/swagger"
    echo ""
    echo "🐳 Container Status:"
    docker compose ps
    echo ""
    echo "📋 To view logs: docker compose logs -f"
    echo "🛑 To stop: docker compose down"
else
    echo "❌ Service failed to start properly. Check logs:"
    docker compose logs
    exit 1
fi