# Deploy slim bundle to the VS Code extension's bin directory

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ProjectDir)
$src = Join-Path $RepoRoot 'publish\calcpad-server-slim'
$dst = Join-Path $RepoRoot 'Calcpad.Web\frontend\vscode-calcpad\bin'

if (-not (Test-Path $src)) {
    Write-Host "Source not found: $src" -ForegroundColor Red
    Write-Host "Run 'Server: Build Slim Bundle' first." -ForegroundColor Yellow
    exit 1
}

if (Test-Path $dst) {
    Remove-Item -Recurse -Force $dst
}

Copy-Item -Recurse $src $dst
Write-Host "Deployed to vscode-calcpad\bin\" -ForegroundColor Green
