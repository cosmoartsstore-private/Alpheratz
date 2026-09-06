param(
    [switch]$UseCachedVcRedist
)

$ErrorActionPreference = "Stop"

$BuildWorksRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $BuildWorksRoot
$InstallerScriptPath = Join-Path $BuildWorksRoot "nsis\Installer.nsi"
$InstallerOutputPath = Join-Path $BuildWorksRoot "Alpheratz-Installer.exe"
$VcRedistPath = Join-Path $BuildWorksRoot "runtime\vc_redist.x64.exe"
# Windows App Runtime preflight is intentionally skipped for the self-contained build.
# WindowsAppSDKSelfContained=true copies WinAppSDK dependencies into the app output.

if (Test-Path $InstallerOutputPath) {
    Remove-Item -LiteralPath $InstallerOutputPath -Force
}

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

& "$PSScriptRoot\prepare-vc-redist.ps1" `
    -DestinationPath $VcRedistPath `
    -UseCached:$UseCachedVcRedist

$productVersionText = (Get-Item -LiteralPath $VcRedistPath).VersionInfo.ProductVersion
$versionMatch = [regex]::Match($productVersionText, "\d+\.\d+\.\d+(?:\.\d+)?")
if (!$versionMatch.Success) {
    throw "Visual C++ Redistributable version could not be read: $productVersionText"
}

$parsedVersion = [Version]$versionMatch.Value
$revision = if ($parsedVersion.Revision -ge 0) { $parsedVersion.Revision } else { 0 }
$VcRedistVersion = "{0}.{1}.{2}.{3}" -f `
    $parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $revision

& $MakensisPath "/DVC_REDIST_VERSION=$VcRedistVersion" $InstallerScriptPath
if ($LASTEXITCODE -ne 0) {
    throw "makensis に失敗しました。終了コード: $LASTEXITCODE"
}

if (!(Test-Path $InstallerOutputPath)) {
    throw "Installer output is missing: $InstallerOutputPath"
}

Write-Host "Build release OK"
Write-Host "Installer OK: $InstallerOutputPath"
Write-Host "Bundled Visual C++ Redistributable: $VcRedistVersion"
