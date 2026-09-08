<#
.SYNOPSIS
    Smoke-tests a real Dynamics connection using the packaged WhoAmI payload.

.DESCRIPTION
    Runs the same check as Integration.Tests.ps1 but without a Pester dependency,
    so it works under Windows PowerShell 5.1 - which is the point, because 5.1 is
    what an Azure DevOps agent hosts. Testing under pwsh 7 does not exercise the
    same assembly-load path.

    Reads the connection string from $env:CRM_ONLINE_CONNSTR or $env:CRM_ONPREM_CONNSTR
    so it never appears on a command line or in shell history.

.EXAMPLE
    # In a Windows PowerShell 5.1 window:
    $env:CRM_ONLINE_CONNSTR = 'AuthType=ClientSecret;Url=https://contoso.crm4.dynamics.com;ClientId=...;ClientSecret=...'
    .\tests\Test-Connection.ps1

.EXAMPLE
    .\tests\Test-Connection.ps1 -Target OnPrem
#>
[CmdletBinding()]
param(
    [ValidateSet('Online', 'OnPrem')]
    [string] $Target = 'Online',

    # Defaults to the packaged task payload - the exact DLL set that ships.
    [string] $CmdletDll
)

$ErrorActionPreference = 'Stop'

function Fail($msg) { Write-Host "FAIL  $msg" -ForegroundColor Red; exit 1 }
function Pass($msg) { Write-Host "PASS  $msg" -ForegroundColor Green }
function Info($msg) { Write-Host "      $msg" -ForegroundColor DarkGray }

if ($PSVersionTable.PSEdition -eq 'Core') {
    Fail "Run this under Windows PowerShell 5.1, not pwsh $($PSVersionTable.PSVersion). The cmdlets are net462 assemblies and 5.1 is what the build agent hosts - testing elsewhere proves nothing about the agent."
}
Pass "Windows PowerShell $($PSVersionTable.PSVersion)"

$envVar  = if ($Target -eq 'Online') { 'CRM_ONLINE_CONNSTR' } else { 'CRM_ONPREM_CONNSTR' }
$connStr = [Environment]::GetEnvironmentVariable($envVar)
if (-not $connStr) {
    Fail "`$env:$envVar is not set. Set it in this window first (see the examples in Get-Help .\tests\Test-Connection.ps1 -Full)."
}

# Report the auth mode without ever echoing secrets.
$authType = if ($connStr -match 'AuthType\s*=\s*([^;]+)') { $Matches[1].Trim() } else { '(unspecified)' }
$url      = if ($connStr -match 'Url\s*=\s*([^;]+)')      { $Matches[1].Trim() } else { '(unspecified)' }
Pass "$envVar is set"
Info "AuthType : $authType"
Info "Url      : $url"

if (-not $CmdletDll) {
    $CmdletDll = @(
        (Join-Path $PSScriptRoot '..\Extension\Implementation\WhoAmI\ps_modules\CI\Innofactor.Crm.CI.dll'),
        (Join-Path $PSScriptRoot '..\Cmdlets\FindCrmUser\bin\Release\Innofactor.Crm.CI.dll')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $CmdletDll) { Fail "No Innofactor.Crm.CI.dll found. Build the solution in Release, or run Extension\pack.ps1 first." }

Import-Module (Resolve-Path $CmdletDll) -ErrorAction Stop
Pass "Loaded $((Resolve-Path $CmdletDll).Path)"

Write-Host ''
Write-Host "Connecting..." -ForegroundColor Cyan

try {
    # Deliberately NOT -Verbose: the cmdlet logs the full connection string at
    # verbose level, which would put the secret in your console and in any transcript.
    $user = Find-CrmUser -ConnectionString $connStr
}
catch {
    Write-Host ''
    Fail "$($_.Exception.Message)"
}

if (-not $user -or $user.UserId -eq [guid]::Empty) { Fail 'Connected but WhoAmI returned no user.' }

Write-Host ''
Pass 'WhoAmI succeeded'
Info "UserId         : $($user.UserId)"
Info "BusinessUnitId : $($user.BusinessUnitId)"
Info "OrganizationId : $($user.OrganizationId)"

# The whole reason the ADAL version is pinned - confirm what actually got loaded.
Write-Host ''
$loaded = [AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { $_.GetName().Name -in 'Microsoft.IdentityModel.Clients.ActiveDirectory', 'Microsoft.Xrm.Tooling.Connector' }

foreach ($a in $loaded) { Info ("{0} {1}" -f $a.GetName().Name, $a.GetName().Version) }

$adal = $loaded | Where-Object { $_.GetName().Name -eq 'Microsoft.IdentityModel.Clients.ActiveDirectory' }
if ($authType -match 'AD|IFD') {
    Info 'On-prem AD/IFD uses WS-Trust and does not load ADAL - that is expected.'
}
elseif (-not $adal) {
    Fail 'ADAL was never loaded on an OAuth connection - unexpected; investigate before publishing.'
}
elseif ($adal.GetName().Version.ToString() -ne '3.19.8.16603') {
    Fail "ADAL $($adal.GetName().Version) loaded, expected 3.19.8.16603. The pin has drifted."
}
else {
    Pass 'ADAL 3.19.8.16603 loaded - the pin holds'
}

Write-Host ''
Write-Host "$Target connection verified." -ForegroundColor Green
