[CmdletBinding()]

$JsPath = Get-VstsInput -Name jsPath
$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition
$gulpFile = Join-Path $scriptPath "gulpfile.mjs"
$packageFile = Join-Path $scriptPath "package.json"

Write-Verbose "Script Path    : $scriptPath"
Write-Host "Gulp File      : $gulpFile"
Write-Host "JavaScript Path: $JsPath"
Write-Host "Package File   : $packageFile"

Write-Verbose "Copying $gulpFile to $JsPath"
Copy-Item $gulpFile $JsPath
Write-Verbose "Copying $packageFile to $JsPath"
Copy-Item $packageFile $JsPath

Set-Location $JsPath

Write-Host "Installing Gulp"
npm install
if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE" }

Write-Verbose "Calling gulp minify"
$gulpFile = Join-Path $JsPath "gulpfile.mjs"
npx gulp --gulpfile $gulpFile minify
if ($LASTEXITCODE -ne 0) { throw "gulp minify failed with exit code $LASTEXITCODE" }


$packageFile = Join-Path $JsPath "package.json"
$packageLockFile = Join-Path $JsPath "package-lock.json"

Write-Host "Removing $gulpFile"
Remove-Item $gulpFile -Force
Remove-Item $packageFile -Force
Remove-Item $packageLockFile -Force

Write-Host "Removing node_modules folder"
$nodeModules = Join-Path $JsPath "node_modules"
Remove-Item -Path $nodeModules -Recurse -Force

Write-Verbose "Done"
