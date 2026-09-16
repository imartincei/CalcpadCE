# beforeDevCommand for `tauri dev` on Windows: build the frontend, start the
# rebuild watchers, and don't return until calcpad-web\dist is complete.
#
# Replaces build-frontend.ps1 -Watch, which leaked a detached tsc watcher on
# every run and ran the web watcher in the foreground, so tauri never waited for
# dist and the webview raced the first build. Accumulated watchers all empty and
# rewrite dist at once: the app then loads broken assets, cargo hits
# `Text file busy`, and vite dies on `ENOTEMPTY ...\dist\assets`.
#
# `-Stop` kills the watchers without starting any.
param(
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalNpm  = Join-Path $ScriptDir 'local-npm.ps1'
$FrontendLog = Join-Path $env:TEMP 'calcpad-frontend-watch.log'
$WebLog      = Join-Path $env:TEMP 'calcpad-web-watch.log'
$Dist        = Join-Path $ScriptDir '..\calcpad-web\dist'
$BuildTimeoutS = 180

# Matched on the command line rather than a recorded PID: npm exits once node is
# up, so a stored root PID has no tree left for taskkill to walk.
function Stop-Watchers {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -match 'calcpad-(web|frontend)' -and
            $_.CommandLine -match 'build:watch|tsc.*--watch|run\s+watch'
        } |
        ForEach-Object { & taskkill.exe /PID $_.ProcessId /T /F 2>$null | Out-Null }
}

function Start-Watcher([string]$Prefix, [string]$Script, [string]$Log) {
    if (Test-Path $Log) { Remove-Item $Log -Force -ErrorAction SilentlyContinue }
    Start-Process -PassThru -WindowStyle Hidden -FilePath 'powershell' `
        -WorkingDirectory $ScriptDir `
        -RedirectStandardOutput $Log -RedirectStandardError "$Log.err" `
        -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $LocalNpm,
                      '--prefix', $Prefix, 'run', $Script
}

# Fails fast on a build error or a dead watcher instead of sitting out the timeout.
function Wait-ForBuild($Proc, [string]$Log, [string]$Label) {
    $deadline = (Get-Date).AddSeconds($BuildTimeoutS)
    while ($true) {
        $text = if (Test-Path $Log) { Get-Content $Log -Raw -ErrorAction SilentlyContinue } else { '' }
        $errText = if (Test-Path "$Log.err") { Get-Content "$Log.err" -Raw -ErrorAction SilentlyContinue } else { '' }
        if ($text -match 'built in') { return }
        if ("$text$errText" -match 'error during build|ENOTEMPTY' -or $Proc.HasExited) {
            Write-Error "dev-frontend: $Label failed`n$text`n$errText"
        }
        if ((Get-Date) -gt $deadline) {
            Write-Error "dev-frontend: $Label did not finish in ${BuildTimeoutS}s; see $Log"
        }
        Start-Sleep -Milliseconds 500
    }
}

Push-Location $ScriptDir
try {
    Stop-Watchers

    # calcpad-web compiles against calcpad-frontend's emitted output, so that lands
    # first - and `run build` also runs the postbuild codegen `tsc --watch` never does.
    & $LocalNpm --prefix ../calcpad-frontend run build
    if ($LASTEXITCODE -ne 0) { throw "calcpad-frontend build failed ($LASTEXITCODE)" }

    Start-Watcher '../calcpad-frontend' 'watch' $FrontendLog | Out-Null
    $web = Start-Watcher '../calcpad-web' 'build:watch' $WebLog

    # No separate `calcpad-web run build`: the watcher's own initial build is the one
    # build needed, and waiting for it keeps the webview off a half-written dist.
    Wait-ForBuild $web $WebLog 'calcpad-web build'

    # "built in" can precede the files settling, so confirm the entry the page loads.
    $entry = $null
    foreach ($i in 1..40) {
        $index = Join-Path $Dist 'index.html'
        if (Test-Path $index) {
            $m = [regex]::Match((Get-Content $index -Raw), 'assets/index-[A-Za-z0-9_-]+\.js')
            if ($m.Success -and (Test-Path (Join-Path $Dist $m.Value))) { $entry = $m.Value; break }
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $entry) { throw "$Dist is incomplete after the build" }

    Write-Host "dev-frontend: dist ready ($entry)"
}
finally {
    Pop-Location
}
