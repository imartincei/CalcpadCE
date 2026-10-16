# Wrapper that runs npm using the project-local Node 24 (Active LTS) in .node\,
# so we don't inherit whatever Node version happens to be on the user's PATH.
# Used by tauri.conf.json's beforeDevCommand / beforeBuildCommand on Windows.
$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:PATH = "$ScriptDir\.node;$env:PATH"
# Call npm.cmd, not the bare `npm` (which PowerShell resolves to npm.ps1).
# Splatting the automatic $args into the .ps1 shim silently drops every arg.
& npm.cmd @args
exit $LASTEXITCODE
