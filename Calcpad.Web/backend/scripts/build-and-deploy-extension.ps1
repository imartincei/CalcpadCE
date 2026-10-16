# Build Calcpad.Server in Debug, then copy the four runtime DLLs into the
# vscode-calcpad extension's bin/ folder.
#
# Why this script exists: the extension spawns Calcpad.Server.exe DETACHED so
# it can be shared across VS Code windows (see calcpad-frontend/.../server-
# manager.ts). That means ending a debug session leaves the server alive,
# which locks every DLL in vscode-calcpad/bin/ and makes the next dev cycle
# fail with "The process cannot access the file ... because it is being used
# by another process."
#
# The server writes its PID to {bin}/.calcpad-server.lock. We read that, kill
# the recorded process, wait for handles to release, then proceed.

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$BackendCsproj  = Join-Path $RepoRoot 'Calcpad.Web\backend\Calcpad.Server.csproj'
$BackendBinDir  = Join-Path $RepoRoot ('Calcpad.Web\backend\bin\' + $Configuration + '\net10.0')
$ExtensionRoot  = Join-Path $RepoRoot 'Calcpad.Web\frontend\vscode-calcpad'
$ExtensionBin   = Join-Path $ExtensionRoot 'bin'
$LockFile       = Join-Path $ExtensionBin '.calcpad-server.lock'

function Stop-BundledServer {
    if (-not (Test-Path $LockFile)) { return }

    $pidToKill = $null
    try {
        $lock = Get-Content $LockFile -Raw | ConvertFrom-Json
        if ($lock.pid -is [int] -or $lock.pid -is [long]) { $pidToKill = [int]$lock.pid }
    } catch {
        # malformed lock - fall through to delete
    }

    if ($null -ne $pidToKill) {
        $proc = Get-Process -Id $pidToKill -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "[deploy] stopping bundled server pid=$pidToKill"
            try { Stop-Process -Id $pidToKill -Force -ErrorAction Stop } catch { }

            # Poll up to 5 s for Windows to release the file handles.
            $deadline = (Get-Date).AddSeconds(5)
            while ((Get-Date) -lt $deadline) {
                if (-not (Get-Process -Id $pidToKill -ErrorAction SilentlyContinue)) { break }
                Start-Sleep -Milliseconds 100
            }
        }
    }

    Remove-Item $LockFile -Force -ErrorAction SilentlyContinue
}

function Copy-WithRetry {
    param([string]$Source, [string]$Destination)

    # Antivirus / search indexer can hold freshly-released DLLs briefly even
    # after the owning process exits. Retry on IOException.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            Copy-Item -Path $Source -Destination $Destination -Force -ErrorAction Stop
            return
        } catch [System.IO.IOException] {
            if ($attempt -eq 6) { throw }
            Start-Sleep -Milliseconds (150 * $attempt)
        }
    }
}

Write-Host "[deploy] building $BackendCsproj ($Configuration)"
dotnet build $BackendCsproj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

Stop-BundledServer

if (-not (Test-Path $ExtensionBin)) { New-Item -ItemType Directory -Path $ExtensionBin -Force | Out-Null }

$dlls = @('Calcpad.Core.dll', 'Calcpad.Server.dll', 'Calcpad.Highlighter.dll', 'Calcpad.OpenXml.dll')
foreach ($dll in $dlls) {
    $src = Join-Path $BackendBinDir $dll
    if (-not (Test-Path $src)) { throw "Build produced no $dll at $src" }
    Copy-WithRetry -Source $src -Destination $ExtensionBin
}

Write-Host "[deploy] OK - copied $($dlls.Count) DLLs to $ExtensionBin"

# The extension's runtime is dist/extension.js, compiled from src/*.ts. If we
# only ship updated DLLs without recompiling TS, the running extension keeps
# the old request payloads / shapes and bug-fixes in extension.ts silently
# don't take effect. Always recompile after a DLL deploy so the bundle stays
# in sync.
Write-Host "[deploy] compiling extension TypeScript -> dist/extension.js"
Push-Location $ExtensionRoot
try {
    & npm run compile
    if ($LASTEXITCODE -ne 0) { throw "npm run compile failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}
Write-Host "[deploy] OK - extension bundle rebuilt"
