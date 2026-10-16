# Build a portable single-file Calcpad.Server.exe for Windows.
#
# Assets (template.html, Fonts, UiAssets) are embedded resources with a
# disk fallback, and the shipped appsettings.json values match the code
# defaults, so the exe needs nothing beside it. Everything else publish
# emits is pruned to leave a single file.

param(
    [string]$Rid = 'win-x64',
    [switch]$NoCompression
)

$ErrorActionPreference = 'Stop'

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$RepoRoot   = Split-Path -Parent (Split-Path -Parent $ProjectDir)
$OutputDir  = Join-Path $RepoRoot 'publish\calcpad-server-portable'
$CsprojPath = Join-Path $ProjectDir 'Calcpad.Server.csproj'

Write-Host "Building portable Calcpad.Server.exe..." -ForegroundColor Blue
Write-Host "Runtime identifier: $Rid" -ForegroundColor Yellow
Write-Host "Output directory:   $OutputDir" -ForegroundColor Yellow

if (Test-Path $OutputDir) {
    Write-Host "Cleaning previous portable build..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutputDir
}

Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $CsprojPath -r $Rid

Write-Host "Publishing self-contained single file..." -ForegroundColor Yellow
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = 1
dotnet publish $CsprojPath `
    -c Release `
    -r $Rid `
    --self-contained true `
    -o $OutputDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$(if ($NoCompression) { 'false' } else { 'true' }) `
    -p:DebugType=none `
    -p:GenerateFullPaths=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$Exe = Join-Path $OutputDir 'Calcpad.Server.exe'
if (-not (Test-Path $Exe)) {
    Write-Host "Build failed - Calcpad.Server.exe not found" -ForegroundColor Red
    exit 1
}

Write-Host "Pruning loose publish output..." -ForegroundColor Yellow
Get-ChildItem $OutputDir -Force |
    Where-Object { $_.FullName -ne $Exe } |
    Remove-Item -Recurse -Force

$SizeMb = [math]::Round((Get-Item $Exe).Length / 1MB, 1)
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Portable executable: $Exe ($SizeMb MB)" -ForegroundColor Green
Write-Host "Run: .\Calcpad.Server.exe [--urls http://127.0.0.1:9420]" -ForegroundColor Green
