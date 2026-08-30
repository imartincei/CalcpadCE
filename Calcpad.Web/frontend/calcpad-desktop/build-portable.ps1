# Build a portable (no-installer) Windows build of CalcpadCE.
#
# Publishes Calcpad.Server via sync-bundled-server.mjs, runs `tauri build
# --no-bundle` (Tauri copies bundle.resources next to the exe at compile time
# regardless of bundling — see BaseDirectory::Resource, which resolves to the
# exe's own directory on Windows), then assembles everything next to the exe
# into a flat folder and zips it. Tauri has no built-in "portable" target for
# Windows (only msi/nsis installers), hence the manual assembly.

param(
    [string]$Rid = 'win-x64',
    [string]$Target = 'x86_64-pc-windows-msvc'
)

$ErrorActionPreference = 'Stop'
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot    = Resolve-Path (Join-Path $ScriptDir '..\..\..')
$SyncScript  = Join-Path $RepoRoot 'Calcpad.Web\frontend\vscode-calcpad\scripts\sync-bundled-server.mjs'
$BinariesDir = Join-Path $ScriptDir 'src-tauri\binaries'
$ReleaseDir  = Join-Path $ScriptDir "src-tauri\target\$Target\release"
$OutputDir   = Join-Path $ScriptDir 'src-tauri\target\portable'
$StageDir    = Join-Path $OutputDir 'CalcpadCE-portable-win64'
$ZipPath     = Join-Path $OutputDir 'CalcpadCE-portable-win64.zip'

Write-Host ">> Cargo target: $Target"
Write-Host ">> .NET RID:     $Rid"

New-Item -ItemType Directory -Force -Path $BinariesDir | Out-Null
node $SyncScript "--target=$BinariesDir" "--rid=$Rid" '--configuration=Release' '--keep-skia-natives'

Push-Location $ScriptDir
try {
    npx tauri build --config src-tauri/tauri.windows.conf.json --target $Target --no-bundle
}
finally {
    Pop-Location
}

if (-not (Test-Path $ReleaseDir)) {
    throw "Expected release output at $ReleaseDir"
}

Remove-Item -Recurse -Force $StageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

$excludedExtensions = '.pdb', '.d', '.lib', '.rlib', '.log'
# stage-sidecar mirrors the publish tree into target\<profile>\ for the dev
# loop, and the server writes logs there when run from it. Neither belongs in
# a shipped zip: the logs carry worksheet content and local source paths.
$excludedNames = 'appsettings.Development.json'
Get-ChildItem -Path $ReleaseDir -File |
    Where-Object {
        $_.Extension -notin $excludedExtensions -and
        $_.Name -notin $excludedNames -and
        -not $_.Name.StartsWith('.cargo-')
    } |
    Copy-Item -Destination $StageDir

foreach ($sub in 'bg', 'zh', 'Fonts') {
    $srcSub = Join-Path $ReleaseDir $sub
    if (Test-Path $srcSub) {
        Copy-Item -Recurse -Path $srcSub -Destination (Join-Path $StageDir $sub)
    }
}

$exeSrc = Join-Path $StageDir 'calcpad-desktop.exe'
$exeDst = Join-Path $StageDir 'CalcpadCE.exe'
if (Test-Path $exeSrc) {
    Move-Item -Force -Path $exeSrc -Destination $exeDst
}

Remove-Item -Force $ZipPath -ErrorAction SilentlyContinue
Compress-Archive -Path $StageDir -DestinationPath $ZipPath -Force
Remove-Item -Recurse -Force $StageDir

Write-Host ">> Portable zip: $ZipPath"
