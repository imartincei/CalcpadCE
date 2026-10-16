#!/bin/bash

# Build script for Calcpad.Server slim bundle (for VS Code extension)
# Publishes a framework-dependent build without platform-specific binaries.
# The extension should download on first activation:
#   - SkiaSharp native assets (v3.119.1) → runtimes/{rid}/native/

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}Building Calcpad.Server slim bundle...${NC}"

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
REPO_ROOT="$(dirname "$(dirname "$PROJECT_DIR")")"
OUTPUT_DIR="$REPO_ROOT/publish/calcpad-server-slim"

echo -e "${YELLOW}Project directory: ${PROJECT_DIR}${NC}"
echo -e "${YELLOW}Output directory:  ${OUTPUT_DIR}${NC}"

# Clean previous build
if [ -d "$OUTPUT_DIR" ]; then
    echo -e "${YELLOW}Cleaning previous slim build...${NC}"
    rm -rf "$OUTPUT_DIR"
fi

# Restore dependencies
echo -e "${YELLOW}Restoring NuGet packages...${NC}"
dotnet restore "$PROJECT_DIR/Calcpad.Server.csproj"

# Publish framework-dependent
echo -e "${YELLOW}Publishing framework-dependent build...${NC}"
dotnet publish "$PROJECT_DIR/Calcpad.Server.csproj" \
    -c Release \
    -o "$OUTPUT_DIR"

# Remove SkiaSharp native runtimes (extension downloads these per-platform)
if [ -d "$OUTPUT_DIR/runtimes" ]; then
    echo -e "${YELLOW}Removing SkiaSharp native runtimes...${NC}"
    rm -rf "$OUTPUT_DIR/runtimes"
fi

# Remove .pdb debug symbols
echo -e "${YELLOW}Removing debug symbols...${NC}"
find "$OUTPUT_DIR" -name "*.pdb" -delete

# Verify build
if [ -f "$OUTPUT_DIR/Calcpad.Server.dll" ]; then
    echo -e "${GREEN}Build completed successfully!${NC}"
    echo -e "${GREEN}Entry point: ${OUTPUT_DIR}/Calcpad.Server.dll${NC}"

    # Show total size
    TOTAL_SIZE=$(du -sh "$OUTPUT_DIR" | cut -f1)
    echo -e "${GREEN}Bundle size: ${TOTAL_SIZE}${NC}"

    echo ""
    echo -e "${BLUE}Published files:${NC}"
    ls -lh "$OUTPUT_DIR"

    echo ""
    echo -e "${GREEN}Usage:${NC}"
    echo -e "${GREEN}  dotnet ${OUTPUT_DIR}/Calcpad.Server.dll${NC}"
    echo ""
    echo -e "${YELLOW}Note: Platform-specific assets must be provided before use:${NC}"
    echo -e "${YELLOW}  SkiaSharp native assets (v3.119.1) -> runtimes/{rid}/native/${NC}"
    echo -e "${YELLOW}    Linux x64:   SkiaSharp.NativeAssets.Linux   -> libSkiaSharp.so${NC}"
    echo -e "${YELLOW}    Windows x64: SkiaSharp.NativeAssets.Win32   -> libSkiaSharp.dll${NC}"
    echo -e "${YELLOW}    macOS:       SkiaSharp.NativeAssets.macOS   -> libSkiaSharp.dylib${NC}"
    echo -e "${YELLOW}  PDF generation requires Chromium/Chrome/Edge:${NC}"
    echo -e "${YELLOW}    Set BROWSER_PATH to an existing installation${NC}"
else
    echo -e "${RED}Build failed - Calcpad.Server.dll not found${NC}"
    exit 1
fi
