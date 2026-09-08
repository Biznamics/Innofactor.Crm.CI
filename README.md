# Dynamics 365 CE DevOps tools

Build and release tooling for Microsoft Dynamics 365 Customer Engagement and Dataverse —
solution and configuration-data transport, plugin assembly and package updates, web resource
deployment, JavaScript minification and assembly obfuscation.

Works against **Dynamics 365 Online / Dataverse** and **Dynamics CRM on-premises**.

The same engine ships three ways:

| | What it is | Use it when |
| --- | --- | --- |
| **Azure Pipelines tasks** | [DevOps for Microsoft Dynamics 365](https://marketplace.visualstudio.com/items?itemName=InnofactorSE.cinteros-devutils-ci-build-tasks) on the Visual Studio Marketplace | Automating build and release pipelines |
| **PowerShell cmdlets** | The `Innofactor.Crm.CI` module in this repo | Scripting outside a pipeline, or local testing |
| **Shuffle Builder / Runner** | XrmToolBox tools from [rappen/Xrm.Shuffle](https://github.com/rappen/Xrm.Shuffle) | Authoring a Shuffle definition, or running one by hand |

---

## Contents

- [Install](#install)
- [Pipeline tasks](#pipeline-tasks)
- [Connection strings](#connection-strings)
- [Shuffle in depth](#shuffle-in-depth)
- [PowerShell cmdlets](#powershell-cmdlets)
- [Build from source](#build-from-source)
- [Credits](#credits)

---

## Install

### Azure Pipelines

Install [DevOps for Microsoft Dynamics 365](https://marketplace.visualstudio.com/items?itemName=InnofactorSE.cinteros-devutils-ci-build-tasks)
into your Azure DevOps organisation. The tasks then appear in the task picker prefixed
`D365CE DevOps:`, and are referenced from YAML by their task name (see below).

Tasks run on the **PowerShell3** handler and need a **Windows** agent.

### Shuffle Builder / Runner (XrmToolBox)

These are not in the XrmToolBox Tool Library. Download the release from
[rappen/Xrm.Shuffle](https://github.com/rappen/Xrm.Shuffle), unblock the zip before extracting
(right-click → Properties → Unblock), and drop the `.dll` into the `Plugins` folder of your
XrmToolBox installation. Both tools then appear in your toolbox.

- **Shuffle Builder** — a no-code editor for the definition file: which entities, which
  attributes, in what order, with which filters and relationships.
- **Shuffle Runner** — executes a definition against the connected environment, exporting or
  importing.

### From source

```bash
git clone --recurse-submodules https://github.com/imranakram/Innofactor.Crm.CI.git
cd Innofactor.Crm.CI
```

See [Build from source](#build-from-source).

---

## Pipeline tasks

All nine tasks, with their YAML task name and major version:

| Task | YAML | Purpose |
| --- | --- | --- |
| Shuffle Export | `ShuffleExport@9` | Export solutions and/or data per a Shuffle definition |
| Shuffle Import | `ShuffleImport@9` | Import solutions and/or data per a Shuffle definition |
| Update Assembly | `UpdateAssembly@9` | Update a registered plugin assembly |
| Update Package | `UpdatePackage@9` | Update a registered plugin package |
| Update Web Resources | `UpdateWebResources@9` | Push web resources from a folder structure |
| Apply Build Version | `ApplyVersionToAssemblies@9` | Stamp the build version into `AssemblyInfo.cs` |
| Minify JavaScripts | `MinifyJS@8` | Minify `*.maxi.js` into `*.js` |
| Obfuscate Assembly | `ObfuscateAssembly@9` | Obfuscate an assembly with ConfuserEx |
| WhoAmI | `WhoAmI@9` | Connectivity smoke test |

> **Store the connection string as a secret variable** and pass it as `$(CrmConnectionString)`.
> Never inline credentials in YAML.

### A complete release pipeline

Export from dev, then import into test:

```yaml
trigger:
  branches:
    include: [ main ]

pool:
  vmImage: windows-latest

variables:
  - group: dynamics-connections   # holds DevConnectionString / TestConnectionString as secrets

steps:
  # Fail fast with a clear error if the connection is wrong
  - task: WhoAmI@9
    displayName: Verify DEV connection
    inputs:
      crmConnectionString: $(DevConnectionString)

  - task: ShuffleExport@9
    displayName: Export solution and config data from DEV
    inputs:
      crmConnectionString: $(DevConnectionString)
      definitionFile: $(Build.SourcesDirectory)/shuffle/release.xml
      dataFile: $(Build.ArtifactStagingDirectory)/release.data.xml
      setVersion: true

  - publish: $(Build.ArtifactStagingDirectory)
    artifact: shuffle

  - task: ShuffleImport@9
    displayName: Import into TEST
    inputs:
      crmConnectionString: $(TestConnectionString)
      definitionFile: $(Build.SourcesDirectory)/shuffle/release.xml
      dataFile: $(Build.ArtifactStagingDirectory)/release.data.xml
```

If `dataFile` is omitted it defaults to the definition file path with the extension changed to
`.data.xml` — so `release.xml` pairs with `release.data.xml`.

### Build pipeline: version, minify, obfuscate, deploy

```yaml
steps:
  # Stamps the build number into every AssemblyInfo.cs it finds
  - task: ApplyVersionToAssemblies@9
    inputs:
      versionType: build            # 'build' parses $(Build.BuildNumber); 'file' reads versionFile
      workingDirectory: $(Build.SourcesDirectory)/src
      versionMatch: false           # true replaces the first two digits with the CRM SDK
                                    # version found in packages.config

  - task: VSBuild@1
    inputs:
      solution: '**/*.sln'
      configuration: Release

  # Minifies every *.maxi.js in the folder to the same name without '.maxi'
  - task: MinifyJS@8
    inputs:
      jsPath: $(Build.SourcesDirectory)/webresources/scripts

  - task: ObfuscateAssembly@9
    inputs:
      assembly: $(Build.SourcesDirectory)/src/Plugins/bin/Release/My.Plugins.dll
      level: '2'                    # 0 lightest .. 4 extreme; costs size and performance
      key: $(Build.SourcesDirectory)/src/Plugins/My.snk

  - task: UpdateAssembly@9
    inputs:
      crmConnectionString: $(CrmConnectionString)
      assembly: $(Build.SourcesDirectory)/src/Plugins/bin/Release/My.Plugins.dll
      updateManaged: false          # true to allow updating a managed assembly

  - task: UpdatePackage@9
    inputs:
      crmConnectionString: $(CrmConnectionString)
      packageName: My.Plugin.Package
      packageFile: $(Build.SourcesDirectory)/src/Package/bin/Release/My.Plugin.Package.nupkg
      updateManaged: false
```

`UpdateAssembly` and `UpdatePackage` **update** an existing registration — the assembly or
package must already be registered in the target organisation.

### Web resources

```yaml
  - task: UpdateWebResources@9
    inputs:
      crmConnectionString: $(CrmConnectionString)
      prefix: new                   # must match the publisher prefix of the existing resources
      rootPath: $(Build.SourcesDirectory)/webresources
      pattern: $(Build.SourcesDirectory)/webresources/pattern.txt
      updateManaged: false
```

`rootPath` is the local folder corresponding to the web resource root in CRM. `pattern` points at
a text file of include/exclude globs, one per line, where `!` negates. This pairs neatly with
`MinifyJS` — ship the minified output and skip the sources:

```
scripts\*.js
!**\*.maxi.js
images\*.*
!scripts\test.js
```

Web resources must already be registered.

---

## Connection strings

Every CRM-facing task takes a standard
[Dynamics 365 connection string](https://learn.microsoft.com/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect).
The `AuthType` decides which authentication stack is used underneath.

**Online — service principal (recommended for pipelines):**

```
AuthType=ClientSecret;Url=https://contoso.crm4.dynamics.com;ClientId=<app id>;ClientSecret=<secret>
```

**Online — username and password:**

```
AuthType=OAuth;Url=https://contoso.crm4.dynamics.com;Username=svc@contoso.com;Password=<pw>;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=app://58145B91-0C36-4500-8554-080854F2AC97;LoginPrompt=Never
```

**On-premises — Active Directory:**

```
AuthType=AD;Url=http://crmserver/contoso;Domain=CONTOSO;Username=svcaccount;Password=<pw>
```

**On-premises — internet-facing deployment:**

```
AuthType=IFD;Url=https://contoso.crm.local/contoso;Domain=CONTOSO;Username=svcaccount;Password=<pw>
```

> **Why the ADAL version is pinned.** `Microsoft.Xrm.Tooling.Connector` 4.0.0.0 — from
> `Microsoft.CrmSdk.XrmTooling.CoreAssembly` **9.1.1.65**, the newest published — is compiled
> against `Microsoft.IdentityModel.Clients.ActiveDirectory` **3.19.8.16603** exactly. Strong-name
> binding then demands that exact version, and overriding it requires a binding redirect in the
> *host process* config (`powershell.exe.config` on the agent), which an extension cannot modify.
> A library `.dll.config` is ignored — that is why no `.config` ships in the package. So the pin
> is a requirement, not a workaround.
>
> This is **not** a PowerShell limitation: ADAL 5.x loads into PS 5.1 perfectly well on its own.
> And since no newer XrmTooling binds a newer ADAL, the only way off ADAL 3.x is off
> `CrmServiceClient` altogether — which
> [Microsoft's transition guidance](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/sdk-client-transition)
> advises against while on-prem AD/IFD support is required, since
> `Microsoft.PowerPlatform.Dataverse.Client` never implemented it.

---

## Shuffle in depth

Shuffle moves **solutions** and **configuration data** between environments, driven by a single
declarative XML definition. Its stand-out capability is handling **N:N (many-to-many)
relationships** and cross-entity lookups that most data-migration tools cannot.

A run has two halves:

- **Export** reads from the source environment and writes a **data file** (`*.data.xml`).
- **Import** reads that data file and writes into the target environment.

The *same definition file* drives both. The definition says what the data is and how to match it;
the data file is only the payload.

### Definition anatomy

```xml
<ShuffleDefinition Timeout="600" StopOnError="true">
  <Blocks>
    <SolutionBlock ... />
    <DataBlock ... />
    <!-- blocks execute top to bottom, in document order -->
  </Blocks>
</ShuffleDefinition>
```

| Attribute | Default | Meaning |
| --- | --- | --- |
| `Timeout` | — | Connection timeout in seconds |
| `StopOnError` | `false` | Abort the whole run on first failure instead of continuing |

**Order matters.** Blocks run in the order they appear, and later blocks can reference earlier
ones. Export parents before children, and both participants of an N:N before the intersect.

### DataBlock

```xml
<DataBlock Name="Categories" Entity="category" Type="Entity">
  <Export ActiveOnly="false"> ... </Export>
  <Import CreateWithId="true" Save="CreateUpdate"> ... </Import>
  <Relation Block="..." Attribute="..." />
</DataBlock>
```

| Attribute | Required | Meaning |
| --- | --- | --- |
| `Name` | yes | Identifies the block; how other blocks refer to it |
| `Entity` | yes | Logical name of the entity |
| `Type` | no | `Entity` (default) or `Intersect` for an N:N relationship |
| `IntersectName` | no | Schema name of the N:N relationship, when `Type="Intersect"` |

A block may have `Export`, `Import`, or both. Without `Export` the block contributes nothing to
the data file; without `Import` it is exported but never written to the target.

#### Export

Either an explicit attribute list or raw FetchXML — not both.

```xml
<Export ActiveOnly="true">
  <Filter Attribute="statuscode" Operator="Equal" Type="int" Value="3" />
  <Sort Attribute="title" Type="Asc" />
  <Attributes>
    <Attribute Name="title" />
    <Attribute Name="content" />
    <Attribute Name="statecode" />
    <Attribute Name="statuscode" />
    <Attribute Name="keywords" IncludeNull="true" />
  </Attributes>
</Export>
```

| Element / attribute | Notes |
| --- | --- |
| `ActiveOnly` | Default `false`. Export only active records |
| `Filter` | Multiple filters are **AND**ed. `Type` is one of `string`, `guid`, `int`, `bool`, `datetime`, `null`, `not-null` |
| `Sort` | `Type` is `Asc` (default) or `Desc`. Controls order in the data file — keeps diffs readable in source control |
| `Attributes/Attribute` | `IncludeNull="true"` writes the attribute even when empty, which is how you *clear* a value on import |
| `FetchXML` | Alternative to all of the above; build it with [FetchXML Builder](https://fetchxmlbuilder.com/) |

> **Do not export system-maintained attributes** such as `createdon`, `createdby`, `modifiedon`
> or `modifiedby` — the import will fail. If you export `statuscode` you must also export
> `statecode`, since the pair is validated together.

#### Import

```xml
<Import CreateWithId="true" Save="CreateUpdate" Delete="None"
        UpdateInactive="false" UpdateIdentical="false">
  <Match PreRetrieveAll="true">
    <Attribute Name="title" />
  </Match>
</Import>
```

| Attribute | Default | Meaning |
| --- | --- | --- |
| `CreateWithId` | `false` | Preserve the source record's GUID. Set `true` when workflows, lookups or other records reference these by id |
| `Save` | `CreateUpdate` | `CreateUpdate`, `CreateOnly`, `UpdateOnly`, or `Never` |
| `Delete` | `None` | `None`, `Existing` (delete matched records), `All` (clear the target first) |
| `UpdateInactive` | `false` | Allow updating records that are inactive in the target |
| `UpdateIdentical` | `false` | Write even when nothing changed. Leave `false` to avoid pointless audit noise |
| `Overwrite` | — | **Deprecated.** Use `Save` |

**Match** is what makes an import idempotent. It defines how a record in the data file is
recognised in the target:

- Match on one or more attributes — a title, or a business key such as `new_code`.
- With `CreateWithId="true"` you can match on the primary id instead.
- No `Match` element means every run creates new records.
- `PreRetrieveAll="true"` pre-loads target records in one query rather than querying per record —
  much faster for large sets.

`Save="Never"` is the idiom for a block that exists only so *other* blocks can resolve references
to it. The matching rules stay available but nothing is written — exactly what you want for the
two sides of an N:N whose records already exist in the target.

#### Relation

Filters a block's export by its relationship to an already-exported block, so you take only the
children of the parents you actually exported:

```xml
<DataBlock Name="Tasks" Entity="task">
  <Export> ... </Export>
  <Relation Block="Accounts" Attribute="regardingobjectid" />
</DataBlock>
```

| Attribute | Required | Meaning |
| --- | --- | --- |
| `Block` | yes | `Name` of a previously exported block |
| `Attribute` | yes | Lookup on *this* entity pointing at that block's entity |
| `PK-Attribute` | no | Primary key on the related entity, when it is not the default |
| `IncludeNull` | no | Default `false`. Also include records where the lookup is empty |

### Many-to-many relationships

The case most tools cannot handle. Three blocks, in this order:

1. Both participating entities, as ordinary `Entity` blocks.
2. An `Intersect` block for the relationship itself.

```xml
<!-- 1. Both sides first. Save="Never" if they already exist in the target -->
<DataBlock Name="Articles" Entity="knowledgearticle" Type="Entity">
  <Export>
    <Attributes><Attribute Name="title" /></Attributes>
  </Export>
  <Import CreateWithId="true" Save="Never">
    <Match><Attribute Name="title" /></Match>
  </Import>
</DataBlock>

<DataBlock Name="Categories" Entity="category" Type="Entity">
  <Export>
    <Attributes><Attribute Name="title" /></Attributes>
  </Export>
  <Import CreateWithId="true" Save="Never">
    <Match><Attribute Name="title" /></Match>
  </Import>
</DataBlock>

<!-- 2. Then the relationship. Export exactly the two id columns, nothing else -->
<DataBlock Name="ArticleCategories" Entity="knowledgearticlecategories"
           Type="Intersect" IntersectName="knowledgearticlecategories">
  <Export>
    <Attributes>
      <Attribute Name="knowledgearticleid" />
      <Attribute Name="categoryid" />
    </Attributes>
  </Export>
  <Import />
</DataBlock>
```

Two rules that catch people out:

- The intersect's `Export` must list **exactly the two id attributes** of the related entities.
- The intersect's `Import` takes **no `Match` element**. Shuffle Builder adds one automatically;
  delete it.

### Solution blocks

```xml
<SolutionBlock Name="MySolution" Path="solutions" File="MySolution.zip">
  <Export Type="Managed" PublishBeforeExport="true" SetVersion="{ShuffleVar:version}">
    <Settings Customization="true" General="true" />
  </Export>
  <Import Type="Managed"
          ActivateServersideCode="true"
          OverwriteCustomizations="true"
          PublishAll="true"
          OverwriteSameVersion="true"
          OverwriteNewerVersion="false">
    <PreRequisites>
      <Solution Name="BaseSolution" Comparer="ge" Version="2.1.0.0" />
    </PreRequisites>
  </Import>
</SolutionBlock>
```

**Export** — `Type` is `Managed`, `Unmanaged`, `Both` or `None`. `PublishBeforeExport` publishes
customisations first. `SetVersion` stamps a version into the solution before export;
`TargetVersion` sets the target CRM version. The optional `Settings` element selects which system
settings travel with the solution (`AutoNumbering`, `Calendar`, `Customization`, `EmailTracking`,
`General`, `Marketing`, `OutlookSync`, `RelationshipRoles`, `IsvConfig` — all default `false`).

**Import** — `Type`, `ActivateServersideCode`, `OverwriteCustomizations` and `PublishAll` are all
required. `OverwriteSameVersion` defaults `true`, `OverwriteNewerVersion` defaults `false`.
`PreRequisites` gates the import on other solutions being present, where `Comparer` is one of
`any`, `eq-this`, `ge-this`, `eq`, `ge`. `PostSuccessfulImportBlocks` nests further blocks that
run only when the import succeeded — handy for seeding configuration data right after a solution
lands.

### Build versioning

If a definition contains the literal placeholder `{ShuffleVar:version}` and the `ShuffleExport`
task runs with `setVersion: true` (the default), the task substitutes it with the contents of
`version.txt` from `$(Agent.BuildDirectory)` before exporting. That is how a pipeline stamps the
build number into an exported solution:

```xml
<Export Type="Managed" SetVersion="{ShuffleVar:version}" />
```

### Serialization

The `ShuffleExport` pipeline task always writes **`SimpleWithValue`**: compact XML that also
records clear-text labels for lookups and option sets, so a data file stays readable and
reviewable in a pull request. The `Export-CrmShuffle` cmdlet lets you choose:

| Type | Description |
| --- | --- |
| `Full` | Serialized `EntityCollection` — verbose, lossless |
| `Simple` | Compact XML, ids only |
| `SimpleWithValue` | `Simple` plus clear-text values for lookups and option sets *(pipeline default)* |
| `SimpleNoId` | `SimpleWithValue` without lookup GUIDs — useful for id-independent comparison |

### Practical notes

- Keep definitions small and focused — one per concern (10–15 entities is a comfortable ceiling)
  rather than one definition covering everything.
- Commit both the definition and the exported data file. With `Sort` set and `SimpleWithValue`
  serialization, configuration changes show up as reviewable diffs.
- Adding an attribute to a definition later is fine — re-export and the data file picks it up.
- Run `WhoAmI` as the first step of any pipeline that touches CRM, so a bad connection string
  fails immediately with a clear message.

---

## PowerShell cmdlets

The `Innofactor.Crm.CI` module underlies the pipeline tasks and can be used directly. Every
cmdlet takes `-ConnectionString` and an optional `-Timeout` in seconds.

```powershell
Import-Module .\Cmdlets\FindCrmUser\bin\Release\Innofactor.Crm.CI.dll
```

> Import these under **Windows PowerShell 5.1**. They are .NET Framework 4.6.2 assemblies built
> against the PowerShell 5 reference assemblies — the same host an Azure DevOps agent uses.

| Cmdlet | Purpose |
| --- | --- |
| `Find-CrmUser` | Executes `WhoAmIRequest`; returns `UserId`, `BusinessUnitId`, `OrganizationId` |
| `Export-CrmShuffle` | Exports per a definition. Takes `-Definition` (XmlDocument), `-Folder`, `-Type` |
| `Import-CrmShuffle` | Imports per a definition and data file |
| `Update-CrmAssembly` | Updates a registered plugin assembly |
| `Update-PluginPackage` | Updates a registered plugin package |
| `Update-CrmResources` | Updates web resources from a folder |
| `Publish-Theme` | Publishes a theme |
| `Out-ObfuscatedAssembly` | Obfuscates an assembly with ConfuserEx |

```powershell
$conn = 'AuthType=ClientSecret;Url=https://contoso.crm4.dynamics.com;ClientId=...;ClientSecret=...'

Find-CrmUser -ConnectionString $conn

[xml]$definition = Get-Content .\shuffle\config.xml
$data = Export-CrmShuffle -ConnectionString $conn `
                          -Definition $definition `
                          -Folder .\shuffle `
                          -Type SimpleWithValue
$data.Save('.\shuffle\config.data.xml')
```

---

## Build from source

Requires Visual Studio 2022 or later (or MSBuild), the .NET Framework 4.6.2 targeting pack, and
NuGet. The Shuffle core arrives as a git submodule, so clone with `--recurse-submodules`.

```powershell
nuget restore Innofactor.Crm.CI.sln
msbuild Innofactor.Crm.CI.sln /t:Rebuild /p:Configuration=Release
```

### Packaging the extension

Always clean first — a stale `bin/Release` is how removed dependencies keep getting shipped,
because `pack.ps1` copies `*.dll` out of it wholesale.

```powershell
Get-ChildItem Cmdlets -Directory | ForEach-Object {
  foreach ($sub in 'bin','obj') {
    $dir = Join-Path $_.FullName $sub
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
  }
}
msbuild Innofactor.Crm.CI.sln /t:Rebuild /p:Configuration=Release
.\Extension\pack.ps1
```

`pack.ps1` stages each task's `ps_modules`, downloads the `VstsTaskSdk`, and calls `tfx` to build
(and optionally publish) the `.vsix` into `Extension/VSIX`.

### Tests

```powershell
./tests/Run-Tests.ps1
./tests/Run-Tests.ps1 -VsixPath ./Extension/VSIX/<built>.vsix
```

The suite gates the packaged artifact — no removed dependencies riding along, ADAL still pinned,
task ids and versions never regressing, and every documentation image still resolving. See
[tests/README.md](tests/README.md). **A green run against a freshly built `.vsix` is the release
gate.**

For publishing steps and the rollback procedure, see [RELEASING.md](RELEASING.md).

### Project structure

```
Cmdlets/          PowerShell cmdlet projects (one assembly per task)
Extension/        Azure DevOps extension: manifest, task definitions, packaging
  Implementation/   one folder per task - task.json + script.ps1
  Documentation/    Marketplace overview and images
modules/          Xrm.Shuffle submodule - the Shuffle core, upstream at rappen/Xrm.Shuffle
tests/            Pester suite
```

---

## Credits

Shuffle was created by **[Jonas Rapp](https://jonasr.app/)**, who maintains the core engine and
the XrmToolBox tools at [rappen/Xrm.Shuffle](https://github.com/rappen/Xrm.Shuffle). The DevOps
tooling around it grew out of work at Cinteros and later Innofactor, and is now maintained by
**Imran Akram**.

Background reading:

- Jonas Rapp — [DevOps I: background](https://jonasr.app/devops-i/),
  [II: build and release tasks](https://jonasr.app/devops-ii/),
  [III: end-to-end demo](https://jonasr.app/devops-iii/),
  [public preview](https://jonasr.app/devops-preview/)
- Sara Lagerquist — [Transport data between environments](https://saralagerquist.com/2019/12/02/mvp-advent-calendar-transport-data-between-environments-with-saras-favorite-tool/),
  a walkthrough of Shuffle Builder and Runner covering knowledge articles, categories and the N:N
  between them. Written against an older release, so the screenshots and download location differ
  from the current tools, but the approach still holds.

## Contributing

Changes to the **Shuffle core** (`modules/Xrm.Shuffle`) belong upstream in
[rappen/Xrm.Shuffle](https://github.com/rappen/Xrm.Shuffle) — that is the copy that compiles.
Everything else goes here. Please run `./tests/Run-Tests.ps1` before opening a pull request.

## License

See [LICENCE](LICENCE).
