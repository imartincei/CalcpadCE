# Publish Calcpad.Server for the host RID and mirror the whole publish tree
# into src-tauri\target\{debug,release}\ so BaseDirectory::Resource resolves
# to a directory that already contains the apphost plus every sibling DLL /
# native lib / deps.json / runtimeconfig.json at `tauri dev` time.
#
# See stage-sidecar.sh for the same explanation in POSIX shell.

$ErrorActionPreference = 'Stop'
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot    = Resolve-Path (Join-Path $ScriptDir '..\..\..')
$SyncScript  = Join-Path $RepoRoot 'Calcpad.Web\frontend\vscode-calcpad\scripts\sync-bundled-server.mjs'
$BinariesDir = Join-Path $ScriptDir 'src-tauri\binaries'

$hostLine = (& rustc -vV | Select-String '^host:').ToString()
$Triple   = ($hostLine -split '\s+', 2)[1].Trim()

switch ($Triple) {
    'x86_64-pc-windows-msvc'    { $Rid = 'win-x64' }
    'aarch64-pc-windows-msvc'   { $Rid = 'win-arm64' }
    'x86_64-unknown-linux-gnu'  { $Rid = 'linux-x64' }
    'aarch64-unknown-linux-gnu' { $Rid = 'linux-arm64' }
    'x86_64-apple-darwin'       { $Rid = 'osx-x64' }
    'aarch64-apple-darwin'      { $Rid = 'osx-arm64' }
    default { throw "unsupported host triple: $Triple" }
}

Write-Host ">> Cargo target: $Triple"
Write-Host ">> .NET RID:     $Rid"

New-Item -ItemType Directory -Force -Path $BinariesDir | Out-Null
node $SyncScript "--target=$BinariesDir" "--rid=$Rid" '--configuration=Release' '--keep-skia-natives'

# After the sync: it prunes anything it does not recognise from the target.
Copy-Item (Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.txt') -Destination $BinariesDir -Force
Copy-Item (Join-Path $RepoRoot 'LICENSE') -Destination (Join-Path $BinariesDir 'LICENSE.txt') -Force

if ($Triple -like '*windows*') {
    $apphost = Join-Path $BinariesDir 'Calcpad.Server.exe'
} else {
    $apphost = Join-Path $BinariesDir 'Calcpad.Server'
}
if (-not (Test-Path $apphost)) {
    throw "Expected published apphost at $apphost"
}
Write-Host ">> Publish tree staged at $BinariesDir"

foreach ($profile in @('debug', 'release')) {
    $profDir = Join-Path $ScriptDir "src-tauri\target\$profile"
    if (Test-Path $profDir) {
        Copy-Item -Recurse -Force -Path (Join-Path $BinariesDir '*') -Destination $profDir
        Write-Host ">> Mirrored publish tree into $profDir\"
    }
}
