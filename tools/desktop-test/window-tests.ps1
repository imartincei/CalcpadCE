param([switch]$Stage, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$Desktop = (Resolve-Path (Join-Path $PSScriptRoot '../../Calcpad.Web/frontend/calcpad-desktop')).Path
$Work = Join-Path $PSScriptRoot '.work/window-tests'
$App = Join-Path $Work 'app'
$Helpers = Join-Path $Work 'helpers'
New-Item -ItemType Directory -Force -Path $App, $Helpers | Out-Null

if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot 'deskrun/deskrun.csproj') -c Release -o $Helpers --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'deskrun build failed' }

    if ($Stage -or -not (Test-Path (Join-Path $Desktop 'src-tauri/binaries/Calcpad.Server.exe'))) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Desktop 'stage-sidecar.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'stage-sidecar failed' }
    }

    Push-Location (Join-Path $Desktop '../calcpad-web')
    try {
        npm run build:desktop
        if ($LASTEXITCODE -ne 0) { throw 'frontend build failed' }
    } finally { Pop-Location }

    $previousConfig = $env:TAURI_CONFIG
    $window = (Get-Content (Join-Path $Desktop 'src-tauri/tauri.conf.json') -Raw | ConvertFrom-Json).app.windows[0]
    $window.width = 640
    $window.height = 400
    $env:TAURI_CONFIG = @{
        identifier = 'org.calcpad-ce.window-tests'
        productName = 'CalcpadCE Window Tests'
        bundle = @{ resources = @() }
        app = @{ windows = @($window); security = @{ capabilities = @(
            'default'
            @{
                identifier = 'window-tests'
                windows = @('main*')
                permissions = @('core:window:allow-set-position', 'core:window:allow-set-size')
            }
        ) } }
    } | ConvertTo-Json -Depth 8 -Compress
    Push-Location (Join-Path $Desktop 'src-tauri')
    try {
        cargo rustc --locked --features window-tests --bin calcpad-desktop -- -o (Join-Path $App 'calcpad-window-tests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'desktop build failed' }
    } finally {
        Pop-Location
        $env:TAURI_CONFIG = $previousConfig
    }

    Copy-Item -Recurse -Force -Path (Join-Path $Desktop 'src-tauri/binaries/*') -Destination $App
}

node --test (Join-Path $PSScriptRoot 'windows.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'window tests failed' }
