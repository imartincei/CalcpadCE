# Starts the isolated test app (see build.ps1) on the hidden desktop "CalcpadTest".
#   launch.ps1 [-Settings <appsettings.json path>] [-Env @{ NAME = 'value' }]
#   launch.ps1 -Stop
# Restarts the test app if it is running. Without -Settings the server gets its stock appsettings.json.
# Rust-side stdout/stderr go to .work\app-out.txt. The webview exposes CDP on -CdpPort (see cdp.mjs).
param([string]$Settings, [hashtable]$Env = @{}, [int]$CdpPort = 9223, [switch]$Stop)
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Work = Join-Path $Here '.work'
$Dbg = Join-Path $Work 'app'
$Exe = Join-Path $Dbg 'calcpad-desktop.exe'

function Stop-TestApp {
    $ours = @(Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Work, [StringComparison]::OrdinalIgnoreCase) })
    # The PDF browser the test server launched, with its children; force-killing the server orphans it.
    $ourIds = @($ours | ForEach-Object ProcessId)
    Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe' OR Name='chrome-headless-shell.exe'" |
        Where-Object { $ourIds -contains $_.ParentProcessId } |
        ForEach-Object { & taskkill /T /F /PID $_.ProcessId 2>&1 | Out-Null }
    $ours | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

Stop-TestApp
if ($Stop) { 'stopped'; return }
if (-not (Test-Path $Exe)) { throw "Test app not built; run build.ps1 first" }

$source = if ($Settings) { $Settings } else { Join-Path $Work 'appsettings.default.json' }
Copy-Item -Force $source (Join-Path $Dbg 'appsettings.json')

$appEnv = @{
    WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$CdpPort"
    CALCPAD_LOG_LEVEL = 'verbose'
}
foreach ($name in $Env.Keys) { $appEnv[$name] = $Env[$name] }

# The app inherits this shell's environment; put the caller's values back once it has started.
$saved = @{}
foreach ($name in $appEnv.Keys) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $appEnv[$name])
}
try {
    # deskrun waits on the app: the hidden desktop is destroyed once its last handle closes.
    $deskrun = Join-Path $Work 'bin\deskrun\deskrun.exe'
    Start-Process -FilePath $deskrun -WorkingDirectory $Dbg -WindowStyle Hidden -ArgumentList @(
        'CalcpadTest', '86400', '--log', "`"$(Join-Path $Work 'app-out.txt')`"", '--', "`"$Exe`"")
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

# Ready once the webview answers on the CDP port.
for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-RestMethod "http://127.0.0.1:$CdpPort/json/version" -TimeoutSec 1 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 }
}
if ($i -eq 60) { throw "Webview did not open CDP port $CdpPort; see $Work\app-out.txt" }
"launched: settings=$(if ($Settings) { $Settings } else { 'default' }) cdp=$CdpPort"
