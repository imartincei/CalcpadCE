# Build script for Calcpad.Server slim bundle (for VS Code extension)
# Publishes a framework-dependent build without platform-specific binaries.

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ProjectDir)
$OutputDir = Join-Path $RepoRoot 'publish\calcpad-server-slim'
$CsprojPath = Join-Path $ProjectDir 'Calcpad.Server.csproj'

Write-Host "Building Calcpad.Server slim bundle..." -ForegroundColor Blue
Write-Host "Project directory: $ProjectDir" -ForegroundColor Yellow
Write-Host "Output directory:  $OutputDir" -ForegroundColor Yellow

# Clean previous build
if (Test-Path $OutputDir) {
    Write-Host "Cleaning previous slim build..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutputDir
}

# Restore dependencies
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $CsprojPath

# Publish framework-dependent
Write-Host "Publishing framework-dependent build..." -ForegroundColor Yellow
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = 1
dotnet publish $CsprojPath -c Release -o $OutputDir

# Remove SkiaSharp native runtimes (extension downloads these per-platform)
$RuntimesDir = Join-Path $OutputDir 'runtimes'
if (Test-Path $RuntimesDir) {
    Write-Host "Removing SkiaSharp native runtimes..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $RuntimesDir
}

# Remove Playwright browser cache
$PlaywrightDir = Join-Path $OutputDir '.playwright'
if (Test-Path $PlaywrightDir) {
    Remove-Item -Recurse -Force $PlaywrightDir
}

# Remove .pdb debug symbols
Write-Host "Removing debug symbols..." -ForegroundColor Yellow
Get-ChildItem $OutputDir -Filter '*.pdb' -Recurse | Remove-Item

# Verify build
$EntryPoint = Join-Path $OutputDir 'Calcpad.Server.dll'
if (Test-Path $EntryPoint) {
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "Entry point: $EntryPoint" -ForegroundColor Green
    Write-Host "Slim bundle built to $OutputDir"
} else {
    Write-Host "Build failed - Calcpad.Server.dll not found" -ForegroundColor Red
    exit 1
}
