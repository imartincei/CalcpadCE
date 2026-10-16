$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Archive
. "$PSScriptRoot\install-dotnet.ps1" -Architecture x64

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "calcpad-dotnet-test-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Add-Framework([string]$Name, [string]$Version, [string]$Root = $testRoot) {
    $directory = Join-Path $Root "shared\$Name\$Version"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $directory "$Name.deps.json") '{}'
}

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

try {
    Assert (-not (Test-SharedFramework $testRoot 'Microsoft.NETCore.App' '10.0.0')) 'Missing runtime was accepted.'
    Add-Framework 'Microsoft.NETCore.App' '9.0.9'
    Add-Framework 'Microsoft.NETCore.App' '11.0.0'
    Add-Framework 'Microsoft.NETCore.App' '10.0.0-preview.1'
    Assert (-not (Test-SharedFramework $testRoot 'Microsoft.NETCore.App' '10.0.0')) 'An incompatible runtime was accepted.'
    Add-Framework 'Microsoft.NETCore.App' '10.0.6'
    Assert (Test-SharedFramework $testRoot 'Microsoft.NETCore.App' '10.0.0') 'A compatible patch was rejected.'
    Assert (-not (Test-SharedFramework $testRoot 'Microsoft.NETCore.App' '10.0.7')) 'A runtime below the required patch was accepted.'
    Assert (-not (Test-SharedFramework $testRoot 'Microsoft.AspNetCore.App' '10.0.0')) 'The base runtime satisfied ASP.NET Core.'

    $config = Join-Path $testRoot 'runtimeconfig.json'
    Set-Content -LiteralPath $config '{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.AspNetCore.App","version":"10.0.0"}]}}'
    $testState = @{ Downloads = 0; Checksum = 'verified'; ExitCode = 3010; InstallCalls = 0; SystemRoot = $testRoot }

    function Get-DotNetRoot { return $testState.SystemRoot }
    function Invoke-RestMethod {
        param([string]$Uri)
        Assert ($Uri -match '/10\.0/releases.json$') 'Wrong release channel.'
        return @{
            'latest-runtime' = '10.0.12'
            releases = @(@{
                runtime = @{
                    version = '10.0.12'
                    files = @(
                        @{ rid = 'win-x64'; name = 'dotnet-apphost-pack-test.zip'; url = 'https://example.invalid/apphost'; hash = 'verified' }
                        @{ rid = 'win-x64'; name = 'dotnet-runtime-test.exe'; url = 'https://example.invalid/runtime'; hash = 'verified' }
                        @{ rid = 'win-x64'; name = 'dotnet-runtime-test.zip'; url = 'https://example.invalid/runtime'; hash = 'verified' }
                    )
                }
                'aspnetcore-runtime' = @{
                    version = '10.0.12'
                    files = @(
                        @{ rid = 'win-x64'; name = 'aspnetcore-runtime-test.exe'; url = 'https://example.invalid/runtime'; hash = 'verified' }
                        @{ rid = 'win-x64'; name = 'aspnetcore-runtime-test.zip'; url = 'https://example.invalid/runtime'; hash = 'verified' }
                    )
                }
            })
        }
    }
    function Invoke-WebRequest {
        param($Uri, $OutFile, [switch]$UseBasicParsing)
        $testState.Downloads++
        Set-Content -LiteralPath $OutFile 'test installer'
    }
    function Get-FileHash {
        param($LiteralPath, $Algorithm)
        return [pscustomobject]@{ Hash = $testState.Checksum }
    }
    function Start-Process {
        param($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru, $WindowStyle)
        Assert ($ArgumentList -contains '/quiet' -and $ArgumentList -contains '/norestart') 'Installer is not unattended.'
        $testState.InstallCalls++
        if ($testState.ExitCode -in 0, 3010) {
            $name = if ($FilePath.EndsWith('aspnetcore-runtime-test.exe')) { 'Microsoft.AspNetCore.App' } else { 'Microsoft.NETCore.App' }
            Add-Framework $name '10.0.12'
        }
        return @{ ExitCode = $testState.ExitCode }
    }
    function Expand-Archive {
        param($LiteralPath, $DestinationPath, [switch]$Force)
        Assert ($LiteralPath.EndsWith('.zip')) 'A user installation downloaded a system installer.'
        Assert ($DestinationPath -eq $userRuntimeRoot) 'A user installation wrote outside its runtime directory.'
        $name = if ($LiteralPath.EndsWith('aspnetcore-runtime-test.zip')) { 'Microsoft.AspNetCore.App' } else { 'Microsoft.NETCore.App' }
        Add-Framework $name '10.0.12' $DestinationPath
        Set-Content -LiteralPath (Join-Path $DestinationPath 'dotnet.exe') 'test host'
    }

    Assert ((Install-SharedFrameworks -RuntimeConfig $config -Architecture x64) -eq 3010) 'Reboot requirement was lost.'
    Assert ($testState.Downloads -eq 1) 'An existing framework was downloaded again.'
    Assert ((Install-SharedFrameworks -RuntimeConfig $config -Architecture x64) -eq 0) 'An installed runtime required a reboot.'
    Assert ($testState.Downloads -eq 1) 'A satisfied installation made a network request.'

    $aspNetPath = Join-Path $testRoot 'shared\Microsoft.AspNetCore.App\10.0.12\Microsoft.AspNetCore.App.deps.json'
    Remove-Item -LiteralPath $aspNetPath
    $testState.Checksum = 'corrupt'
    $failed = $false
    try { Install-SharedFrameworks -RuntimeConfig $config -Architecture x64 | Out-Null } catch { $failed = $true }
    Assert $failed 'A corrupt download was accepted.'
    Assert ($testState.InstallCalls -eq 1) 'A corrupt installer was executed.'

    $testState.Checksum = 'verified'
    $testState.ExitCode = 1603
    $failed = $false
    try { Install-SharedFrameworks -RuntimeConfig $config -Architecture x64 | Out-Null } catch { $failed = $true }
    Assert $failed 'A failed runtime installation was accepted.'

    Remove-Item -LiteralPath (Join-Path $testRoot 'shared\Microsoft.NETCore.App\10.0.6\Microsoft.NETCore.App.deps.json')
    $testState.ExitCode = 0
    $testState.Downloads = 0
    Assert ((Install-SharedFrameworks -RuntimeConfig $config -Architecture x64) -eq 0) 'A fresh installation failed.'
    Assert ($testState.Downloads -eq 2) 'A fresh installation did not install both runtimes.'

    $userRuntimeRoot = Join-Path $testRoot 'user-runtime'
    $testState.Downloads = 0
    Assert ((Install-SharedFrameworks -RuntimeConfig $config -Architecture x64 -InstallMode CurrentUser -UserRuntimeRoot $userRuntimeRoot) -eq 0) 'System runtimes were not reused for the current user.'
    Assert ($testState.Downloads -eq 0) 'An unnecessary user runtime was downloaded.'

    $testState.SystemRoot = Join-Path $testRoot 'missing-system-runtime'
    $systemInstallCalls = $testState.InstallCalls
    Assert ((Install-SharedFrameworks -RuntimeConfig $config -Architecture x64 -InstallMode CurrentUser -UserRuntimeRoot $userRuntimeRoot) -eq 0) 'A user runtime installation failed.'
    Assert ($testState.Downloads -eq 2) 'A user installation did not download both runtime archives.'
    Assert ($testState.InstallCalls -eq $systemInstallCalls) 'A user installation ran a system installer.'
    Assert (-not (Test-Path -LiteralPath $testState.SystemRoot)) 'A user installation changed the system runtime directory.'
    Assert ((Install-SharedFrameworks -RuntimeConfig $config -Architecture x64 -InstallMode CurrentUser -UserRuntimeRoot $userRuntimeRoot) -eq 0) 'An existing user runtime was rejected.'
    Assert ($testState.Downloads -eq 2) 'An existing user runtime was downloaded again.'
    Write-Host 'Runtime prerequisite tests passed.'
}
finally {
    $resolvedTestRoot = (Resolve-Path -LiteralPath $testRoot).Path
    if (-not $resolvedTestRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The test directory is outside the temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
