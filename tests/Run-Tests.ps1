<#
.SYNOPSIS
    Runs the Innofactor.Crm.CI test suite.

.DESCRIPTION
    Offline-safe by default: the vsix gate is skipped when no package is
    present, and the live-org tests are skipped unless CRM_ONLINE_CONNSTR /
    CRM_ONPREM_CONNSTR are set.

    Returns a non-zero exit code on failure so it can be wired into CI.

.EXAMPLE
    ./tests/Run-Tests.ps1

.EXAMPLE
    ./tests/Run-Tests.ps1 -VsixPath ./Extension/VSIX/InnofactorSE.cinteros-devutils-ci-build-tasks-9.0.96.vsix
#>
[CmdletBinding()]
param(
    # Package to run the vsix gate against. Defaults to the newest under Extension/VSIX.
    [string] $VsixPath,

    # Skip the tests that need network or node.
    [switch] $Offline,

    [ValidateSet('Normal', 'Detailed', 'Diagnostic')]
    [string] $Output = 'Normal'
)

$ErrorActionPreference = 'Stop'

$pester = Get-Module -ListAvailable Pester |
            Where-Object Version -ge ([version]'5.0.0') |
            Sort-Object Version -Descending |
            Select-Object -First 1

if (-not $pester) {
    Write-Error @'
Pester 5+ is required (Windows ships 3.4.0, which cannot run these tests).

    Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser -Force -SkipPublisherCheck
'@
    exit 2
}

Import-Module $pester.Path -Force
Write-Host "Pester $($pester.Version)" -ForegroundColor DarkGray

$config = New-PesterConfiguration
$config.Run.Path        = $PSScriptRoot
$config.Run.PassThru    = $true
$config.Output.Verbosity = $Output

if ($Offline) {
    $config.Filter.ExcludeTag = 'Network'
}

if ($VsixPath) {
    $config.Run.Container = @(
        New-PesterContainer -Path (Join-Path $PSScriptRoot 'Vsix.Tests.ps1') -Data @{ VsixPath = $VsixPath }
        New-PesterContainer -Path (Get-ChildItem $PSScriptRoot -Filter '*.Tests.ps1' |
            Where-Object Name -ne 'Vsix.Tests.ps1' | Select-Object -ExpandProperty FullName)
    )
    $config.Run.Path = @()
}

$result = Invoke-Pester -Configuration $config

Write-Host ''
Write-Host ("Passed {0}  Failed {1}  Skipped {2}" -f `
    $result.PassedCount, $result.FailedCount, $result.SkippedCount) -ForegroundColor Cyan

exit ([int]($result.FailedCount -gt 0))
