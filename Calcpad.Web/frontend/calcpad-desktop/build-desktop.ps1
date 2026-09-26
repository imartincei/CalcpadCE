# Build the CalcpadCE desktop app (Tauri + embedded ASP.NET Core sidecar)
# on Windows.
#
# Publishes Calcpad.Server via sync-bundled-server.mjs, renames the apphost
# to Tauri's target-triple suffix, then runs `tauri build` to produce signed
# msi + nsis installers (signing kicks in when CALCPAD_SIGN_THUMBPRINT is set;
# see src-tauri/tauri.conf.json).

param(
    [string]$Rid = 'win-x64',
    [string]$Target = 'x86_64-pc-windows-msvc'
)

$ErrorActionPreference = 'Stop'
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot   = Resolve-Path (Join-Path $ScriptDir '..\..\..')
$SyncScript = Join-Path $RepoRoot 'Calcpad.Web\frontend\vscode-calcpad\scripts\sync-bundled-server.mjs'
$BinariesDir = Join-Path $ScriptDir 'src-tauri\binaries'

Write-Host ">> Cargo target: $Target"
Write-Host ">> .NET RID:     $Rid"

New-Item -ItemType Directory -Force -Path $BinariesDir | Out-Null
node $SyncScript "--target=$BinariesDir" "--rid=$Rid" '--configuration=Release' '--keep-skia-natives'

# After the sync: it prunes anything it does not recognise from the target.
Copy-Item (Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.txt') -Destination $BinariesDir -Force
Copy-Item (Join-Path $RepoRoot 'LICENSE') -Destination (Join-Path $BinariesDir 'LICENSE.txt') -Force

$srcExe  = Join-Path $BinariesDir 'Calcpad.Server.exe'
$destExe = Join-Path $BinariesDir "calcpad-server-$Target.exe"
if (-not (Test-Path $srcExe)) {
    throw "Expected published apphost at $srcExe"
}
Copy-Item -Path $srcExe -Destination $destExe -Force
Write-Host ">> Sidecar staged at $destExe"

Push-Location $ScriptDir
try {
    npx tauri build --config src-tauri/tauri.windows.conf.json --target $Target
}
finally {
    Pop-Location
}
