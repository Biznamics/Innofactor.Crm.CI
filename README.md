# Innofactor.Crm.CI

[![Join the chat at https://gitter.im/Biznamics/Innofactor.Crm.CI](https://badges.gitter.im/Innofactor/Innofactor.Crm.CI.svg)](https://gitter.im/Innofactor/Innofactor.Crm.CI?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

A collection of PowerShell cmdlets for Microsoft Dynamics 365 / Dataverse CI/CD automation. These cmdlets enable seamless integration with Azure DevOps pipelines for building, deploying, and managing CRM/Dataverse components.

## Features

- **PowerShell Cmdlets** for Dynamics 365 / Dataverse automation
- **Azure DevOps Integration** via marketplace extension
- **Shuffle Support** for data and solution migrations
- **Assembly & Web Resource Management** for plugin and customization deployments

## Requirements

- .NET Framework 4.6.2
- PowerShell 5.1 or later
- Microsoft Dynamics 365 / Dataverse environment
- Valid connection string to CRM/Dataverse organization

## Installation

### From Source

```powershell
git clone https://github.com/Biznamics/Innofactor.Crm.CI.git
cd Innofactor.Crm.CI
# Build the solution in Visual Studio or via MSBuild
```

### Azure DevOps Extension

Install the extension from the Visual Studio Marketplace:
[Cinteros DevUtils CI Build Tasks](https://marketplace.visualstudio.com/items?itemName=InnofactorSE.cinteros-devutils-ci-build-tasks)

## Available Cmdlets

### Find-CrmUser

Validates the connection to CRM/Dataverse by executing a `WhoAmI` request.

```powershell
Find-CrmUser -ConnectionString "AuthType=OAuth;Url=https://yourorg.crm.dynamics.com;..."
```

**Output:** `WhoAmIResponse` containing OrganizationId, BusinessUnitId, and UserId.

---

### Update-CrmAssembly

Updates an existing plugin assembly in CRM/Dataverse with a new version from disk.

```powershell
Update-CrmAssembly -AssemblyFile "C:\path\to\plugin.dll" -ConnectionString "..."
```

**Parameters:**

| Parameter | Alias | Required | Description |
|-----------|-------|----------|-------------|
| AssemblyFile | DLL, D | Yes | Path to the assembly file |
| UpdateManaged | UM | No | Allow updating managed assemblies |
| ConnectionString | - | Yes | CRM/Dataverse connection string |
| Timeout | - | No | Connection timeout in seconds |

---

### Update-CrmResources

Updates web resources in CRM/Dataverse from a local folder.

```powershell
Update-CrmResources -RootFolder "C:\webresources" -Prefix "new_" -ConnectionString "..."
```

**Parameters:**

| Parameter | Alias | Required | Description |
|-----------|-------|----------|-------------|
| RootFolder | R | Yes | Path to folder containing web resources |
| Prefix | Pre | Yes | Publisher prefix for web resource paths |
| Pattern | P | No | File/folder include/exclude pattern |
| PatternFile | PF | No | File path for include/exclude patterns |
| UpdateManaged | UM | No | Allow updating managed web resources |

---

### Update-PluginPackage

Updates an existing plugin package (NuGet) in Dataverse.

```powershell
Update-PluginPackage -PluginPackageName "MyPluginPackage" -PluginPackageFile "C:\path\to\package.nupkg" -ConnectionString "..."
```

**Parameters:**

| Parameter | Alias | Required | Description |
|-----------|-------|----------|-------------|
| PluginPackageName | PackageName | Yes | Name of the plugin package in Dataverse |
| PluginPackageFile | PackageFile, PF | Yes | Path to the NuGet package file |
| UpdateManaged | UM | No | Allow updating managed plugin packages |

---

### Publish-Theme

Publishes a theme in CRM/Dataverse.

```powershell
Publish-Theme -ThemeId "00000000-0000-0000-0000-000000000000" -ConnectionString "..."
```

**Parameters:**

| Parameter | Required | Description |
|-----------|----------|-------------|
| ThemeId | Yes | GUID of the theme to publish |

---

### Import-CrmShuffle

Imports data and/or solutions using Shuffle definitions.

```powershell
$definition = [xml](Get-Content "shuffle-definition.xml")
Import-CrmShuffle -Definition $definition -ConnectionString "..."
```

**Parameters:**

| Parameter | Alias | Required | Description |
|-----------|-------|----------|-------------|
| Definition | Def, D | Yes | Shuffle definition as XMLDocument |
| DataXml | - | No | Shuffle XML data to import |
| DataCsv | - | No | Shuffle CSV data as string |
| Folder | F | No | Working folder for relative paths |

---

### Export-CrmShuffle

Exports data from CRM/Dataverse using Shuffle definitions.

```powershell
$definition = [xml](Get-Content "shuffle-definition.xml")
Export-CrmShuffle -Definition $definition -Type Full -ConnectionString "..."
```

**Parameters:**

| Parameter | Alias | Required | Description |
|-----------|-------|----------|-------------|
| Definition | Def, D | Yes | Shuffle definition as XMLDocument |
| Type | T | Yes | Export type: Full, Simple, SimpleWithValue, SimpleNoId, Explicit, or Text |
| Folder | F | No | Working folder for relative paths |

---

### Out-ObfuscatedAssembly

Obfuscates a .NET assembly using ConfuserEx.

```powershell
Out-ObfuscatedAssembly -AssemblyFile "C:\path\to\assembly.dll" -ObfuscationLevel 2
```

**Parameters:**

| Parameter | Alias | Required | Description |
|-----------|-------|----------|-------------|
| AssemblyFile | DLL, D | Yes | Path to the assembly file |
| KeyFile | Key, K | No | Path to SNK file for signing |
| ObfuscationLevel | Obfuscation, L | No | Level of obfuscation (ConfuserEx preset) |

## Connection String

All CRM-connected cmdlets require a `ConnectionString` parameter. See [Microsoft documentation](https://docs.microsoft.com/en-us/powershell/module/microsoft.xrm.tooling.crmconnector.powershell/get-crmconnection) for connection string formats.

### Examples

**OAuth (Interactive):**
```
AuthType=OAuth;Url=https://yourorg.crm.dynamics.com;AppId=<appid>;RedirectUri=app://<appid>;LoginPrompt=Auto
```

**Client Secret (Service Principal):**
```
AuthType=ClientSecret;Url=https://yourorg.crm.dynamics.com;ClientId=<appid>;ClientSecret=<secret>
```

**Certificate:**
```
AuthType=Certificate;Url=https://yourorg.crm.dynamics.com;ClientId=<appid>;Thumbprint=<thumbprint>
```

## Shuffle Tools

Shuffle is a powerful data and solution migration framework for Dynamics 365 / Dataverse.

- **Shuffle Builder** and **Shuffle Runner** are available in [XrmToolBox](http://www.xrmtoolbox.com)
- For more information, visit [Xrm.Shuffle](https://github.com/rappen/Xrm.Shuffle)

## Project Structure

```
Innofactor.Crm.CI/
├── Cmdlets/
│   ├── FindCrmUser/           # Find-CrmUser cmdlet
│   ├── UpdateCrmAssembly/     # Update-CrmAssembly cmdlet
│   ├── UpdateCrmResouces/     # Update-CrmResources cmdlet
│   ├── UpdatePackage/         # Update-PluginPackage cmdlet
│   ├── PublishTheme/          # Publish-Theme cmdlet
│   ├── Shuffle/               # Import/Export-CrmShuffle cmdlets
│   ├── OutObfuscatedAssembly/ # Out-ObfuscatedAssembly cmdlet
│   └── XrmCmdletBase.cs       # Base class for CRM-connected cmdlets
├── Extension/
│   ├── Implementation/        # Azure DevOps task implementations
│   ├── VSIX/                  # Packaged extension output
│   ├── pack.ps1               # Extension packaging script
│   └── vss-extension.json     # Extension manifest
└── modules/
    └── Xrm.Shuffle/           # Shuffle submodule
```

## Articles & Resources

- [DevOps for Dynamics 365 - Part I](https://jonasr.app/2017/04/devops-i/) - Introduction to CI/CD tools
- [Azure DevOps Extension](https://marketplace.visualstudio.com/items?itemName=InnofactorSE.cinteros-devutils-ci-build-tasks) - Pipeline tasks for builds and deployments

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See the repository for license information.

### Azure DevOps Pipeline extension
https://marketplace.visualstudio.com/items?itemName=InnofactorSE.cinteros-devutils-ci-build-tasks <br/>
Tasks to facilitate automation of builds and deployments.

### Shuffle Builder and Shuffle Runner
Shuffle Tools are now available on [XrmToolBox](http://www.xrmtoolbox.com) tools library 
Head over to [Xrm.Shuffle](https://github.com/rappen/Xrm.Shuffle) for more information.

