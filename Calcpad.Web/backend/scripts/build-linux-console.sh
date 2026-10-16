#!/bin/bash

# Build script for Calcpad.Server Linux Console Application
# This script builds a self-contained Linux executable that includes
# the PDF generation service with PuppeteerSharp

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}Building Calcpad.Server Linux Console Application...${NC}"

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo -e "${YELLOW}Project directory: ${PROJECT_DIR}${NC}"

# Change to project directory
cd "$PROJECT_DIR"

# Clean previous builds
echo -e "${YELLOW}Cleaning previous builds...${NC}"
if [ -d "bin" ]; then
    rm -rf bin
fi
if [ -d "obj" ]; then
    rm -rf obj
fi

# Restore dependencies
echo -e "${YELLOW}Restoring NuGet packages...${NC}"
dotnet restore "Calcpad.Server.csproj"

# Build for Linux x64
echo -e "${YELLOW}Building Linux x64 console application...${NC}"
dotnet build "Calcpad.Server.csproj" -r linux-x64 -c Release

# Publish self-contained executable
echo -e "${YELLOW}Publishing self-contained Linux executable...${NC}"
dotnet publish "Calcpad.Server.csproj" -r linux-x64 --self-contained true -p:PublishSingleFile=true -c Release

# Check if build was successful
PUBLISH_PATH="$PROJECT_DIR/bin/Release/net10.0/linux-x64/publish"
if [ -f "$PUBLISH_PATH/Calcpad.Server" ]; then
    echo -e "${GREEN}✓ Build completed successfully!${NC}"
    echo -e "${GREEN}Linux executable: ${PUBLISH_PATH}/Calcpad.Server${NC}"

    # Make executable
    chmod +x "$PUBLISH_PATH/Calcpad.Server"

    # Show file size
    FILE_SIZE=$(stat -c%s "$PUBLISH_PATH/Calcpad.Server" 2>/dev/null || echo "unknown")
    if [ "$FILE_SIZE" != "unknown" ]; then
        FILE_SIZE_MB=$((FILE_SIZE / 1024 / 1024))
        echo -e "${GREEN}File size: ${FILE_SIZE_MB}MB${NC}"
    fi

    # List all files in publish directory
    echo -e "${BLUE}Published files:${NC}"
    ls -lh "$PUBLISH_PATH"

    echo ""
    echo -e "${GREEN}Usage:${NC}"
    echo -e "${GREEN}  Run: ./Calcpad.Server [optional_url]${NC}"
    echo -e "${GREEN}  Example: ./Calcpad.Server http://localhost:9420${NC}"
    echo -e "${GREEN}  Default port: 8080${NC}"
    echo ""
    echo -e "${YELLOW}Features included:${NC}"
    echo -e "${YELLOW}  • ASP.NET Core web server with REST API${NC}"
    echo -e "${YELLOW}  • PDF generation with PuppeteerSharp${NC}"
    echo -e "${YELLOW}  • Swagger API documentation at /swagger${NC}"
    echo -e "${YELLOW}  • Console logging${NC}"
    echo -e "${YELLOW}  • Self-contained - no .NET runtime required${NC}"

else
    echo -e "${RED}✗ Build failed - executable not found${NC}"
    echo -e "${RED}Expected path: ${PUBLISH_PATH}/Calcpad.Server${NC}"
    exit 1
fi
