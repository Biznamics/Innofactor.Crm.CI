<#
    Shared helpers for the Innofactor.Crm.CI test suite.
    Dot-source this from each *.Tests.ps1 file.
#>

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot
}

# task.json / vss-extension.json are a mix of BOM and no-BOM files.
# ConvertFrom-Json in Windows PowerShell chokes on a leading BOM, so strip it.
function Read-JsonFile {
    param([Parameter(Mandatory)][string] $Path)

    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) {
        $text = $text.Substring(1)
    }
    $text | ConvertFrom-Json
}

function Get-Manifest {
    param([string] $Root = (Get-RepoRoot))
    Read-JsonFile (Join-Path $Root 'Extension/vss-extension.json')
}

# Returns one object per task folder: Folder, Path, Name, Id, Version ([version]), Json
function Get-TaskDefinition {
    param([string] $Root = (Get-RepoRoot))

    Get-ChildItem (Join-Path $Root 'Extension/Implementation') -Directory | ForEach-Object {
        $taskJson = Join-Path $_.FullName 'task.json'
        if (-not (Test-Path $taskJson)) { return }
        $json = Read-JsonFile $taskJson
        [pscustomobject]@{
            Folder  = $_.Name
            Path    = $taskJson
            Name    = $json.name
            Id      = $json.id
            Version = [version]('{0}.{1}.{2}' -f $json.version.Major, $json.version.Minor, $json.version.Patch)
            Json    = $json
        }
    }
}

function Get-PublishedBaseline {
    Read-JsonFile (Join-Path $PSScriptRoot 'fixtures/published-baseline.json')
}

# Expands a .vsix (a zip) to a fresh temp folder and returns that path.
function Expand-Vsix {
    param([Parameter(Mandatory)][string] $VsixPath)

    $dest = Join-Path ([System.IO.Path]::GetTempPath()) ("vsixtest_" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $VsixPath), $dest)
    $dest
}

# The tasks that ship a compiled cmdlet (ApplyVersionToAssemblies and MinifyJS do not).
function Get-CmdletBackedTasks {
    @(
        'ObfuscateAssembly'
        'ShuffleExport'
        'ShuffleImport'
        'UpdateAssembly'
        'UpdateWebResources'
        'WhoAmI'
        'UpdatePackage'
    )
}

# ADAL must stay at 3.19.8 - see Cmdlets/*/packages.config for why.
$script:ExpectedAdalFileVersion = '3.19.50615.2240'
function Get-ExpectedAdalFileVersion { $script:ExpectedAdalFileVersion }

# Resolves which .vsix the packaging gate should run against: an explicit path
# if given, otherwise the newest package under Extension/VSIX. Returns $null
# when there is nothing to test, so the gate can skip instead of failing.
function Resolve-VsixPath {
    param([string] $VsixPath)

    if ($VsixPath) {
        return $(if (Test-Path $VsixPath) { (Resolve-Path $VsixPath).Path } else { $null })
    }

    $vsixDir = Join-Path (Get-RepoRoot) 'Extension/VSIX'
    if (-not (Test-Path $vsixDir)) { return $null }

    $newest = Get-ChildItem $vsixDir -Filter *.vsix -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) { $newest.FullName } else { $null }
}
