# Builds the Calcpad CLI, then runs compare_renderings.py in a throwaway
# .venv. Any arguments (e.g. -write) are forwarded to compare_renderings.py.

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$VenvDir = Join-Path $ScriptDir '.venv'
$CliBuildDir = Join-Path $RepoRoot 'cli-build'

$exitCode = 0
try {
    Write-Host "Building Calcpad CLI..." -ForegroundColor Blue
    Remove-Item -Recurse -Force $CliBuildDir -ErrorAction SilentlyContinue
    dotnet publish (Join-Path $RepoRoot 'Calcpad.Cli\Calcpad.Cli.csproj') -c Release -o $CliBuildDir

    Write-Host "Setting up Python venv..." -ForegroundColor Blue
    python -m venv $VenvDir
    & (Join-Path $VenvDir 'Scripts\pip.exe') install --quiet beautifulsoup4==4.14.3

    Write-Host "Comparing renderings..." -ForegroundColor Blue
    & (Join-Path $VenvDir 'Scripts\python.exe') (Join-Path $ScriptDir 'compare_renderings.py') @args
    $exitCode = $LASTEXITCODE
} finally {
    Remove-Item -Recurse -Force $VenvDir -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $CliBuildDir -ErrorAction SilentlyContinue
}

exit $exitCode
