<#
    Opt-in tests against real Dynamics organisations.

    Skipped unless the relevant connection string is present, so the rest of
    the suite stays runnable offline:

        $env:CRM_ONLINE_CONNSTR = 'AuthType=ClientSecret;Url=...;ClientId=...;ClientSecret=...'
        $env:CRM_ONPREM_CONNSTR = 'AuthType=AD;Url=http://crm/org;Domain=...;Username=...;Password=...'

    IMPORTANT: these must run under Windows PowerShell 5.1, not PowerShell 7 - the
    cmdlets are net462 assemblies built against the PowerShell 5 reference assemblies,
    which is also what the build agent hosts.

    In practice Install-Module Pester puts Pester 5+ on the pwsh 7 module path only, so
    Windows PowerShell 5.1 sees just the inbox Pester 3.4.0 and cannot run this file.
    Unless you have deliberately installed Pester 5+ for 5.1 as well, use
    tests\Test-Connection.ps1 instead - same assertions, no test framework.

    Both orgs must pass before publishing:
      - online validates the ADAL 3.19.8 pin (OAuth/ClientSecret path)
      - on-prem validates the AD/IFD path, which never touches ADAL
#>

BeforeAll {
    . $PSScriptRoot/TestHelpers.ps1
    $script:Root = Get-RepoRoot

    # Prefer a packaged task payload; fall back to the build output.
    $script:CmdletDll = @(
        (Join-Path $Root 'Extension/Implementation/WhoAmI/ps_modules/CI/Innofactor.Crm.CI.dll'),
        (Join-Path $Root 'Cmdlets/FindCrmUser/bin/Release/Innofactor.Crm.CI.dll')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($CmdletDll) { Import-Module $CmdletDll -ErrorAction Stop }

    $script:IsDesktopPs = $PSVersionTable.PSEdition -ne 'Core'
}

Describe 'Live connection' -ForEach @(
    @{ Label = 'online'; EnvVar = 'CRM_ONLINE_CONNSTR' }
    @{ Label = 'on-prem'; EnvVar = 'CRM_ONPREM_CONNSTR' }
) {

    BeforeAll {
        $script:ConnStr = [Environment]::GetEnvironmentVariable($EnvVar)
    }

    It "connects to the <Label> organisation with Find-CrmUser" -Skip:(-not [Environment]::GetEnvironmentVariable($EnvVar)) {
        if (-not $script:IsDesktopPs) {
            Set-ItResult -Inconclusive -Because 'run under Windows PowerShell 5.1 to match the build agent'
            return
        }
        $CmdletDll | Should -Not -BeNullOrEmpty -Because 'build the solution in Release first'

        # WhoAmI is the smallest task that exercises CrmServiceClient, and
        # therefore the assembly-load path the ADAL pin exists to protect.
        $user = Find-CrmUser -ConnectionString $ConnStr

        $user            | Should -Not -BeNullOrEmpty
        $user.UserId     | Should -Not -Be ([guid]::Empty)
        $user.OrganizationId | Should -Not -Be ([guid]::Empty)
    }
}
