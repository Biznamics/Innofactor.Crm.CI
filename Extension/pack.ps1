Write-Host
Write-Host
Write-Host
Write-Host "Assembling extension..."

$Input	= (Get-Item -Path ".\..\Cmdlets\").FullName
$Temp	  = (Get-Item -Path ".\Implementation\").FullName
$Output = (Get-Item -Path ".\VSIX\").FullName

Write-Host
Write-Host "Cmdlets will be taken from:       '$Input'"
Write-Host "Extension will be assemblied in:  '$Temp'"
Write-Host "Binary result will be stored in:  '$Output'"

Write-Host

$TaskFolders = @(
	"ApplyVersionToAssemblies"
	"MinifyJS"
	"ObfuscateAssembly"
	"ShuffleExport"
	"ShuffleImport"
	"UpdateAssembly"
	"UpdateWebResources"
	"WhoAmI"
	"UpdatePackage"
)

$DestinationFolders = @();

# Creating list of temporary folders needs to be deleted in advance
foreach($Folder in $TaskFolders)
{
  $DestinationFolders += "$Temp$Folder\ps_modules"
}

# Deleting all found temporary folder
foreach($Folder in $DestinationFolders)
{
  if(Test-Path -Path $Folder)
  {
    Remove-Item -Path $Folder -Recurse -Force
  }
}

$CmdletFolders = @(
	""  # Some tasks may not require cmdlet, and in this case the path is empty
	""  # Some tasks may not require cmdlet, and in this case the path is empty
	"OutObfuscatedAssembly"
	"Shuffle"
	"Shuffle"
	"UpdateCrmAssembly"
	"UpdateCrmResouces"
	"FindCrmUser"
	"UpdatePackage"
);

$SourceFolders = @();

foreach($Folder in $CmdletFolders)
{
  if ($Folder -ne "")
  {
    $Folder = "$Input$Folder\bin\Release"
  }

  $SourceFolders += $Folder
}

Write-Host
Write-Host "Downloading latest VSTS SDK..."
Save-Module -Name VstsTaskSdk -Path .
Write-Host "Copying required files to:"

# Check if 'tfx' is available
if (-not (Get-Command tfx -ErrorAction SilentlyContinue)) {
    Write-Host "'tfx' not found. Installing via npm..."

    # Check if npm is available
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw "npm is required to install tfx-cli but was not found. Please install NodeJS first."
    }

    # Install tfx globally
    npm install -g tfx-cli

    # Verify installation
    if (-not (Get-Command tfx -ErrorAction SilentlyContinue)) {
        throw "Failed to install 'tfx'. Please check your environment or install manually using npm install -g tfx-cli."
    }

    Write-Host "'tfx' installed successfully."
} else {
    Write-Host "'tfx' is already installed."
}


for ($i = 0; $i -lt $SourceFolders.Length; $i++)
{
  if ($SourceFolders[$i] -ne "")
  {
    Copy-Item -Path ($SourceFolders[$i]) -Filter "*.dll" -Recurse -Destination ($DestinationFolders[$i] + "\CI\") -Container
  }

  Copy-Item -Path (Get-ChildItem ".\VstsTaskSdk\")[0].FullName -Recurse -Destination ($DestinationFolders[$i] + "\VstsTaskSdk\") -Container

  Write-Host " * " $DestinationFolders[$i]
}

Remove-Item -Path ".\VstsTaskSdk" -Recurse -Force

Write-Host
Write-Host
Write-Host
Write-Host "Packing extension..."

$Arguments = "--manifest-globs .\vss-extension.json --output-path $Output"

if ((Read-Host -Prompt "Update revision? [Y/N]").ToLower() -eq "y")
{
  $Arguments += " --rev-version"
}

if ((Read-Host -Prompt "Publish extension? [Y/N]").ToLower() -eq "y")
{
  if(!(Test-Path -Path ".\pat.txt"))
  {
    $PAT = Read-Host -Prompt "Enter your PAT"
    Add-Content ".\test.txt" $PAT
  }
  else
  {
    $PAT = Get-Content -Path ".\pat.txt"
  }

  $Arguments += " --token $PAT"

  $Command = "tfx extension publish $Arguments"
}
else
{
  $Command = "tfx extension create $Arguments"
}

Invoke-Expression $Command