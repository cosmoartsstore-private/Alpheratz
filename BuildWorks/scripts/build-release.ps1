$ErrorActionPreference = "Stop"

$BuildWorksRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $BuildWorksRoot
$InstallerScriptPath = Join-Path $BuildWorksRoot "nsis\Installer.nsi"
$InstallerOutputPath = Join-Path $BuildWorksRoot "Alpheratz-Installer.exe"
# Windows App Runtime preflight is intentionally skipped for the self-contained build.
# WindowsAppSDKSelfContained=true copies WinAppSDK dependencies into the app output.

$MakensisCandidates = @(
    "C:\Program Files (x86)\NSIS\makensis.exe",
    "C:\Program Files (x86)\NSIS\Bin\makensis.exe"
)

& "$PSScriptRoot\publish-app-release.ps1"
& "$PSScriptRoot\publish-launcher-release.ps1"

$MakensisPath = $MakensisCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($MakensisPath)) {
    throw "makensis.exe was not found. Install NSIS or update scripts\build-release.ps1."
}

& $MakensisPath $InstallerScriptPath

if (!(Test-Path $InstallerOutputPath)) {
    throw "Installer output is missing: $InstallerOutputPath"
}

Write-Host "Build release OK"
Write-Host "Installer OK: $InstallerOutputPath"
