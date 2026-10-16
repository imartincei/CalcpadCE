param(
    [string]$RuntimeConfig,
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,
    [ValidateSet('AllUsers', 'CurrentUser')]
    [string]$InstallMode = 'AllUsers',
    [string]$UserRuntimeRoot
)

$ErrorActionPreference = 'Stop'

function Test-SharedFramework {
    param([string]$Root, [string]$Name, [version]$MinimumVersion)

    $directory = Join-Path $Root "shared\$Name"
    if (-not (Test-Path -LiteralPath $directory)) { return $false }
    foreach ($entry in Get-ChildItem -LiteralPath $directory -Directory) {
        if ($entry.Name -notmatch '^\d+\.\d+\.\d+$') { continue }
        $version = [version]$entry.Name
        if ($version.Major -eq $MinimumVersion.Major -and
            $version.Minor -eq $MinimumVersion.Minor -and
            $version -ge $MinimumVersion -and
            (Test-Path -LiteralPath (Join-Path $entry.FullName "$Name.deps.json"))) {
            return $true
        }
    }
    return $false
}

function Get-DotNetRoot {
    param([string]$Architecture)
    $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
    try {
        $key = $registry.OpenSubKey("SOFTWARE\dotnet\Setup\InstalledVersions\$Architecture")
        if ($null -ne $key) {
            try { return $key.GetValue('InstallLocation') }
            finally { $key.Dispose() }
        }
        return Join-Path $env:ProgramW6432 'dotnet'
    }
    finally { $registry.Dispose() }
}

function Install-SharedFrameworks {
    param(
        [string]$RuntimeConfig,
        [string]$Architecture,
        [ValidateSet('AllUsers', 'CurrentUser')]
        [string]$InstallMode = 'AllUsers',
        [string]$UserRuntimeRoot
    )
    $frameworks = (Get-Content -LiteralPath $RuntimeConfig -Raw | ConvertFrom-Json).runtimeOptions.frameworks
    $runtimeRoot = Get-DotNetRoot $Architecture
    if ($InstallMode -eq 'CurrentUser') {
        $missing = $frameworks | Where-Object { -not (Test-SharedFramework $runtimeRoot $_.name ([version]$_.version)) }
        if (-not $missing -and -not (Test-Path -LiteralPath (Join-Path $UserRuntimeRoot 'dotnet.exe'))) {
            Write-Host 'The required system runtimes are already installed.'
            return 0
        }
        $runtimeRoot = $UserRuntimeRoot
    }
    $rebootRequired = $false
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    foreach ($framework in $frameworks) {
        $minimum = [version]$framework.version
        if (Test-SharedFramework $runtimeRoot $framework.name $minimum) {
            Write-Host "$($framework.name) is already installed."
            continue
        }

        $channel = "$($minimum.Major).$($minimum.Minor)"
        $metadata = Invoke-RestMethod "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json"
        $release = $metadata.releases | Where-Object { $_.runtime.version -eq $metadata.'latest-runtime' } | Select-Object -First 1
        $package = switch ($framework.name) {
            'Microsoft.NETCore.App' { $release.runtime }
            'Microsoft.AspNetCore.App' { $release.'aspnetcore-runtime' }
            default { throw "Unsupported framework: $($framework.name)" }
        }
        $prefix = if ($framework.name -eq 'Microsoft.NETCore.App') { 'dotnet-runtime-' } else { 'aspnetcore-runtime-' }
        $extension = if ($InstallMode -eq 'CurrentUser') { '.zip' } else { '.exe' }
        $file = $package.files | Where-Object { $_.rid -eq "win-$Architecture" -and $_.name.StartsWith($prefix) -and $_.name.EndsWith($extension) } | Select-Object -First 1
        $installer = Join-Path $PSScriptRoot $file.name
        Write-Host "Installing $($framework.name) $($package.version)..."
        Invoke-WebRequest -Uri $file.url -OutFile $installer -UseBasicParsing
        if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -ne $file.hash) {
            throw 'The .NET installer checksum does not match Microsoft release metadata.'
        }
        try {
            if ($InstallMode -eq 'CurrentUser') {
                Expand-Archive -LiteralPath $installer -DestinationPath $runtimeRoot -Force
            }
            else {
                $process = Start-Process -FilePath $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru -WindowStyle Hidden
                if ($process.ExitCode -eq 3010) { $rebootRequired = $true }
                elseif ($process.ExitCode -ne 0) { throw ".NET installation failed with exit code $($process.ExitCode)." }
            }
        }
        finally { Remove-Item -LiteralPath $installer }
        if (-not (Test-SharedFramework $runtimeRoot $framework.name $minimum)) {
            throw "$($framework.name) is still missing after installation."
        }
    }
    if ($rebootRequired) { return 3010 }
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        exit (Install-SharedFrameworks -RuntimeConfig $RuntimeConfig -Architecture $Architecture -InstallMode $InstallMode -UserRuntimeRoot $UserRuntimeRoot)
    }
    catch {
        Write-Output $_.Exception.Message
        exit 1
    }
}
