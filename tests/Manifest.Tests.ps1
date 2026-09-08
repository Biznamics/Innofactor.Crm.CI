<#
    Release-readiness checks that run against the SOURCE TREE (no build needed).

    These are the cheap guards. Most of the trouble this repo has seen -
    republishing a version the gallery already holds, task versions going
    backwards, screenshots 404ing on the listing - is caught here.
#>

# Discovery-time: -ForEach is expanded while Pester builds the test tree, so
# anything it iterates has to be resolved here rather than in BeforeAll.
BeforeDiscovery {
    . $PSScriptRoot/TestHelpers.ps1
    $Tasks = @(Get-TaskDefinition)
}

BeforeAll {
    . $PSScriptRoot/TestHelpers.ps1
    $script:Root     = Get-RepoRoot
    $script:Manifest = Get-Manifest -Root $Root
    $script:Tasks    = @(Get-TaskDefinition -Root $Root)
    $script:Baseline = Get-PublishedBaseline
}

Describe 'Extension manifest identity' {

    It 'keeps the publisher id (changing it orphans every existing install)' {
        $Manifest.publisher | Should -BeExactly 'InnofactorSE'
    }

    It 'keeps the extension id (it is half of the Marketplace itemName)' {
        $Manifest.id | Should -BeExactly 'cinteros-devutils-ci-build-tasks'
    }

    It 'is categorised as Azure Pipelines' {
        $Manifest.categories | Should -Contain 'Azure Pipelines'
    }

    It 'declares a version newer than what is already published' {
        [version]$Manifest.version |
            Should -BeGreaterThan ([version]$Baseline.publishedVersion) `
            -Because "the gallery already holds $($Baseline.publishedVersion); tfx rejects a republish of the same or a lower version"
    }

    It 'points its repository links at the maintained repo' {
        $Manifest.links.repository.uri | Should -Not -Match 'Biznamics|/Innofactor/'
        $Manifest.repository.uri       | Should -Not -Match 'Biznamics|/Innofactor/'
    }
}

Describe 'Task definitions' {

    It 'has a contribution for every task folder, and a folder for every contribution' {
        $contributions = @($Manifest.contributions |
            Where-Object { $_.type -eq 'ms.vss-distributed-task.task' } |
            ForEach-Object { Split-Path $_.properties.name -Leaf })

        ($contributions | Sort-Object) |
            Should -Be (($Tasks.Folder) | Sort-Object)
    }

    It 'never changes a task id - <_.Name>' -ForEach $Tasks {
        $known = $Baseline.tasks.$($_.Name)
        $known | Should -Not -BeNullOrEmpty -Because "'$($_.Name)' is not in the published baseline; a brand new task is fine, but confirm it deliberately"
        $_.Id  | Should -BeExactly $known.id `
            -Because 'a changed task id silently detaches every pipeline step already referencing it'
    }

    It 'never lowers a task version - <_.Name>' -ForEach $Tasks {
        $known = $Baseline.tasks.$($_.Name)
        if ($known) {
            $_.Version | Should -BeGreaterOrEqual ([version]$known.version)
        }
    }

    It 'has no leftover Innofactor branding in display fields - <_.Name>' -ForEach $Tasks {
        "$($_.Json.friendlyName) $($_.Json.author) $($_.Json.description)" |
            Should -Not -Match 'Innofactor'
    }

    It 'uses the PowerShell3 handler - <_.Name>' -ForEach $Tasks {
        $_.Json.execution.PowerShell3.target | Should -BeExactly 'script.ps1'
    }
}

Describe 'Marketplace overview page' {

    BeforeDiscovery {
        . $PSScriptRoot/TestHelpers.ps1
        $overview = Get-Content (Join-Path (Get-RepoRoot) 'Extension/Documentation/overview.md') -Raw
        $ImageUrls = @([regex]::Matches($overview, '!\[[^\]]*\]\(([^)]+)\)') |
                       ForEach-Object { $_.Groups[1].Value })
    }

    BeforeAll {
        $script:OverviewPath = Join-Path $Root 'Extension/Documentation/overview.md'
        $script:Overview     = Get-Content $OverviewPath -Raw
        $script:ImageUrls    = @([regex]::Matches($Overview, '!\[[^\]]*\]\(([^)]+)\)') |
                                 ForEach-Object { $_.Groups[1].Value })
    }

    It 'references at least one screenshot' {
        $ImageUrls.Count | Should -BeGreaterThan 0
    }

    It 'uses absolute image URLs' {
        # The Marketplace renders this markdown on its own origin and does not
        # rebase relative paths, so 'images/x.png' resolves to
        # marketplace.visualstudio.com/images/x.png and 404s.
        $relative = @($ImageUrls | Where-Object { $_ -notmatch '^https?://' })
        $relative | Should -BeNullOrEmpty -Because 'relative paths render as broken images on the listing'
    }

    It 'serves every referenced image - <_>' -Tag 'Network' -ForEach $ImageUrls {
        $response = Invoke-WebRequest -Uri $_ -Method Head -MaximumRedirection 5 -UseBasicParsing -TimeoutSec 30
        $response.StatusCode | Should -Be 200
    }

    It 'has no dead innofactor.se links' {
        $Overview | Should -Not -Match 'innofactor\.se'
    }
}
