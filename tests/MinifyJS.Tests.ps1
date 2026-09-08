<#
    Behavioural tests for the MinifyJS task.

    Runs the real pipeline the task runs on an agent (npm ci + gulp minify)
    against a fixture, in a throwaway folder. Requires node/npm and network
    access for the first install.
#>

BeforeAll {
    . $PSScriptRoot/TestHelpers.ps1
    $script:Root      = Get-RepoRoot
    $script:TaskDir   = Join-Path $Root 'Extension/Implementation/MinifyJS'
    $script:ScriptPs1 = Get-Content (Join-Path $TaskDir 'script.ps1') -Raw
    $script:HasNode   = [bool](Get-Command node -ErrorAction SilentlyContinue) -and
                        [bool](Get-Command npm  -ErrorAction SilentlyContinue)
}

Describe 'MinifyJS task script' {

    It 'ships a package-lock.json' {
        Join-Path $TaskDir 'package-lock.json' | Should -Exist
    }

    It 'copies the lockfile to the agent' {
        # Without this the lock is never present on the agent and npm ci cannot work.
        $ScriptPs1 | Should -Match 'Copy-Item\s+\$packageLockFile'
    }

    It 'installs with npm ci, not npm install' {
        # npm install re-resolves the ^ ranges on every run, so the committed
        # lock (and any security fix in it) would be ignored on the agent.
        $ScriptPs1 | Should -Match '(?m)^\s*npm ci\s*$'
        $ScriptPs1 | Should -Not -Match '(?m)^\s*npm install\s*$'
    }

    It 'fails the task if install fails' {
        $ScriptPs1 | Should -Match 'if \(\$LASTEXITCODE -ne 0\) \{ throw'
    }

    It 'cleans up after itself' {
        foreach ($item in '\$gulpFile', '\$packageFile', '\$packageLockFile', '\$nodeModules') {
            $ScriptPs1 | Should -Match "Remove-Item.*$item"
        }
    }
}

Describe 'MinifyJS end to end' -Skip:(-not $script:HasNode) {

    BeforeAll {
        $script:Work = Join-Path ([System.IO.Path]::GetTempPath()) ("minify_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $Work -Force | Out-Null

        # Mirror exactly what script.ps1 stages onto the agent.
        Copy-Item (Join-Path $TaskDir 'gulpfile.mjs')      $Work
        Copy-Item (Join-Path $TaskDir 'package.json')      $Work
        Copy-Item (Join-Path $TaskDir 'package-lock.json') $Work
        Copy-Item (Join-Path $PSScriptRoot 'fixtures/minify/sample.maxi.js') $Work

        Push-Location $Work
        try {
            $script:CiOutput   = & npm ci 2>&1 | Out-String
            $script:CiExitCode = $LASTEXITCODE
            if ($CiExitCode -eq 0) {
                $script:GulpOutput   = & npx gulp --gulpfile (Join-Path $Work 'gulpfile.mjs') minify 2>&1 | Out-String
                $script:GulpExitCode = $LASTEXITCODE
            }
        } finally {
            Pop-Location
        }

        $script:MinifiedPath = Join-Path $Work 'sample.js'
        $script:Minified     = if (Test-Path $MinifiedPath) { Get-Content $MinifiedPath -Raw } else { $null }
    }

    AfterAll {
        if ($Work -and (Test-Path $Work)) { Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'installs cleanly from the lockfile' {
        $CiExitCode | Should -Be 0 -Because "npm ci output:`n$CiOutput"
    }

    It 'runs the minify task' {
        $GulpExitCode | Should -Be 0 -Because "gulp output:`n$GulpOutput"
    }

    It 'writes the minified file with .maxi dropped' {
        $MinifiedPath | Should -Exist
    }

    It 'leaves the source file untouched' {
        Join-Path $Work 'sample.maxi.js' | Should -Exist
    }

    It 'actually shrinks the file' {
        (Get-Item $MinifiedPath).Length |
            Should -BeLessThan (Get-Item (Join-Path $Work 'sample.maxi.js')).Length
    }

    It 'strips console calls' {
        $Minified | Should -Not -Match 'console\.log'
    }

    It 'strips debugger statements' {
        $Minified | Should -Not -Match '\bdebugger\b'
    }

    It 'preserves ES2020 syntax rather than choking on it' {
        # terser replaced gulp-uglify precisely because uglify could not parse these.
        $Minified | Should -Match '\?\?'
        $Minified | Should -Not -BeNullOrEmpty
    }

    It 'produces valid JavaScript' {
        & node --check $MinifiedPath 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 0
    }
}

Describe 'MinifyJS dependency hygiene' -Skip:(-not $script:HasNode) {

    It 'has no known vulnerabilities in the lockfile' {
        Push-Location $TaskDir
        try {
            $audit = & npm audit --package-lock-only --json 2>&1 | Out-String
        } finally {
            Pop-Location
        }
        $total = ($audit | ConvertFrom-Json).metadata.vulnerabilities.total
        $total | Should -Be 0 -Because 'run: npm audit fix --package-lock-only'
    }
}
