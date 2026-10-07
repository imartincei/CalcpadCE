# Builds the helpers and an isolated debug copy of the desktop app under .work\.
#   -Stage     re-publish Calcpad.Server into src-tauri\binaries first (after backend changes)
#   -Frontend  rebuild calcpad-frontend + calcpad-web dist first (after TS changes)
# The copy uses identifier com.calcpadce.desktop.test, so it has its own app data folder and
# single-instance lock and never touches the developer's own CalcpadCE.
param([switch]$Stage, [switch]$Frontend)
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Work = Join-Path $Here '.work'
$Desktop = Resolve-Path (Join-Path $Here '..\..\Calcpad.Web\frontend\calcpad-desktop')
New-Item -ItemType Directory -Force -Path $Work | Out-Null

dotnet build (Join-Path $Here 'deskrun\deskrun.csproj') -c Release -o (Join-Path $Work 'bin\deskrun') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'deskrun build failed' }

if ($Stage) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Desktop 'stage-sidecar.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'stage-sidecar failed' }
}
if ($Frontend) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Desktop 'build-frontend.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'build-frontend failed' }
}

$env:CARGO_TARGET_DIR = Join-Path $Work 'tauri-target'
$env:TAURI_CONFIG = '{"identifier":"com.calcpadce.desktop.test","productName":"CalcpadCE Test"}'
Push-Location (Join-Path $Desktop 'src-tauri')
try {
    cargo build
    if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }
} finally { Pop-Location }

# Resource dir == exe dir for a debug build, so the server publish tree sits next to the exe.
$Binaries = Join-Path $Desktop 'src-tauri\binaries'
if (-not (Test-Path (Join-Path $Binaries 'Calcpad.Server.exe'))) { throw "No server in $Binaries; rerun with -Stage" }
Copy-Item -Recurse -Force -Path (Join-Path $Binaries '*') -Destination (Join-Path $Work 'tauri-target\debug')
Copy-Item -Force (Join-Path $Work 'tauri-target\debug\appsettings.json') (Join-Path $Work 'appsettings.default.json')
"Built $Work\tauri-target\debug\calcpad-desktop.exe"
