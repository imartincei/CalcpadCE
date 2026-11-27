# Lint or render CalcpadCE files with the headless Calcpad.Server.
#   tools/server/calcpad-server.ps1 lint [-Info] <file.cpd> ...
#   tools/server/calcpad-server.ps1 render <file.cpd> <out.txt>
# $env:CALCPAD_SERVER picks the server binary; $env:CALCPAD_URL reuses a running server.
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet('lint', 'render')][string]$Command,
    [Parameter(Mandatory, Position = 1, ValueFromRemainingArguments)][string[]]$Paths,
    [switch]$Info
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$utf8 = New-Object System.Text.UTF8Encoding $false
$headers = @{}
if ($env:CALCPAD_API_TOKEN) { $headers['X-Calcpad-Token'] = $env:CALCPAD_API_TOKEN }

function Start-CalcpadServer {
    if ($env:CALCPAD_URL) { return @{ Url = $env:CALCPAD_URL.TrimEnd('/'); Process = $null } }
    $server = $env:CALCPAD_SERVER
    if (-not $server) {
        $arch = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
        $server = foreach ($config in 'Release', 'Debug') {
            foreach ($sub in '', "win-$arch") {
                Get-Item (Join-Path $root "Calcpad.Web\backend\bin\$config\net10.0\$sub\Calcpad.Server.exe") -ErrorAction SilentlyContinue
            }
        }
        $server = $server | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $server -or -not (Test-Path $server)) { Write-Error 'Calcpad.Server not found. Build Calcpad.Web/backend, or set CALCPAD_SERVER.'; exit 2 }
    $portFile = Join-Path ([IO.Path]::GetTempPath()) "calcpad-port-$([guid]::NewGuid()).txt"
    $process = Start-Process $server -PassThru -WindowStyle Hidden -WorkingDirectory (Split-Path $server) `
        -ArgumentList '--port-file', "`"$portFile`"", '--parent-pid', $PID, '--no-exit-on-stdin-close'
    $deadline = (Get-Date).AddSeconds(60)
    while (-not ((Test-Path $portFile) -and (Get-Content $portFile -Raw))) {
        if ($process.HasExited) { Write-Error 'Calcpad.Server exited before it started.'; exit 2 }
        if ((Get-Date) -gt $deadline) { $process.Kill(); Write-Error 'Timed out waiting for Calcpad.Server.'; exit 2 }
        Start-Sleep -Milliseconds 100
    }
    $url = (Get-Content $portFile -TotalCount 1).Trim().TrimEnd('/')
    Remove-Item $portFile -ErrorAction SilentlyContinue
    return @{ Url = $url; Process = $process }
}

function Invoke-Calcpad($url, $route, $body) {
    $bytes = $utf8.GetBytes(($body | ConvertTo-Json -Compress))
    $deadline = (Get-Date).AddSeconds(30)
    while ($true) {
        try {
            return Invoke-WebRequest "$url$route" -Method Post -Body $bytes -Headers $headers `
                -ContentType 'application/json; charset=utf-8' -UseBasicParsing -TimeoutSec 180
        } catch [System.Net.WebException] {
            if ($_.Exception.Response -or (Get-Date) -gt $deadline) { throw }
            Start-Sleep -Milliseconds 150
        }
    }
}

function Read-Body($response) {
    $stream = New-Object IO.MemoryStream
    $response.RawContentStream.CopyTo($stream)
    $utf8.GetString($stream.ToArray())
}

function Get-Request($file, $extra) {
    $path = (Resolve-Path $file).Path
    $body = @{ content = [IO.File]::ReadAllText($path); sourceFilePath = $path }
    foreach ($key in $extra.Keys) { $body[$key] = $extra[$key] }
    $body
}

$session = Start-CalcpadServer
try {
    if ($Command -eq 'lint') {
        $errors = 0
        foreach ($file in $Paths) {
            $result = Read-Body (Invoke-Calcpad $session.Url '/api/calcpad/lint' (Get-Request $file @{})) | ConvertFrom-Json
            $rows = @(@($result.diagnostics) + @($result.includeOnly) | Where-Object { $_ } | ForEach-Object {
                [pscustomobject]@{ Line = $_.line + 1; Text = "$($_.line + 1):$($_.column + 1) [linter] $("$($_.severity)".ToLower()) $($_.code) $($_.message)"; Code = $_.code; Severity = "$($_.severity)".ToLower() }
            } | Where-Object { $Info -or $_.Severity -ne 'information' })
            # Runtime errors come from running the worksheet; their lines are already 1-based.
            $run = Invoke-Calcpad $session.Url '/api/calcpad/convert' (Get-Request $file @{ includeLineAnchors = $true; write = $false })
            $raw = $run.Headers['X-Calcpad-Errors']
            if ($raw) {
                foreach ($e in ([Uri]::UnescapeDataString("$raw") | ConvertFrom-Json)) {
                    if ($rows | Where-Object { $_.Line -eq $e.sourceLine -and $_.Code -eq $e.code -and $_.Severity -eq 'error' }) { continue }
                    $rows += [pscustomobject]@{ Line = $e.sourceLine; Text = "$($e.sourceLine) [runtime] error $($e.code) $($e.message)"; Code = $e.code; Severity = 'error' }
                }
            }
            Write-Output $file
            foreach ($row in $rows | Sort-Object Line) {
                if ($row.Severity -eq 'error') { $errors++ }
                Write-Output "  $($row.Text)"
            }
        }
        Write-Output "$errors error(s)"
        exit ([int]($errors -gt 0))
    }

    if ($Paths.Count -ne 2) { Write-Error 'render takes <file.cpd> <out.txt>'; exit 2 }
    $request = Get-Request $Paths[0] @{ forPrint = $true; includeLineAnchors = $true; write = $false }
    $html = Read-Body (Invoke-Calcpad $session.Url '/api/calcpad/convert' $request)
    $text = $html -replace '(?is)<(script|style)\b.*?</\1\s*>', '' -replace '(?s)<!--.*?-->', '' `
        -replace '(?i)</t[dh]\s*>', ' | ' -replace '(?i)<br\s*/?>|</(p|div|tr|h[1-6]|li)\s*>', "`n" -replace '<[^>]+>', ''
    $text = [Net.WebUtility]::HtmlDecode($text) -replace '[ \t ]+', ' ' -replace ' *\n *', "`n" -replace '\n\s*\n+', "`n"
    [IO.File]::WriteAllText($Paths[1], $text.Trim(), $utf8)
    $errorLines = @($text -split "`n" | Where-Object { $_ -like 'Error in *' })
    $errorLines | Write-Output
    Write-Output "$($Paths[1]) ($($errorLines.Count) calculation error(s))"
} finally {
    if ($session.Process -and -not $session.Process.HasExited) { $session.Process.Kill() }
}
