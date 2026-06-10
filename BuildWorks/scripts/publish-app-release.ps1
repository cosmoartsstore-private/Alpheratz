$ErrorActionPreference = "Stop"

$BuildWorksRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $BuildWorksRoot
$FrontendProjectPath = Join-Path $RepoRoot "app\Alpheratz.Frontend.csproj"
$AppPublishRoot = Join-Path $BuildWorksRoot "artifacts\publish\Alpheratz"
$XbfSourceRoot = Join-Path $RepoRoot "app\artifacts\obj\x64\Release\net8.0-windows10.0.19041.0\win-x64"
$MakePri = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makepri.exe"

if (Test-Path $AppPublishRoot) {
    Remove-Item $AppPublishRoot -Recurse -Force
}

# Build a self-contained frontend package.
# The CLI flag keeps the script explicit even when the project file already sets it.
& dotnet publish $FrontendProjectPath -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $AppPublishRoot

$FrontendExe = Join-Path $AppPublishRoot "Alpheratz.Frontend.exe"
if (!(Test-Path $FrontendExe)) {
    throw "Missing frontend executable: $FrontendExe"
}

# dotnet publish does not copy XBF/XAML files for WinExe projects.
# WinUI3 unpackaged apps resolve ms-appx:/// URIs from the file system. The XAML
# compiler emits URIs of the form ms-appx:///{AssemblyName}/{path}.xaml; at runtime
# WinUI3 prefers compiled XBF (which carries x:Name bindings) over text XAML.
# Place XBF files under an assembly-name subdirectory keeping the .xbf extension.
$AssemblyName = "Alpheratz.Frontend"
$XamlDestRoot = Join-Path $AppPublishRoot $AssemblyName
$xbfFiles = Get-ChildItem $XbfSourceRoot -Recurse -Filter "*.xbf"
foreach ($f in $xbfFiles) {
    $rel = $f.FullName.Substring($XbfSourceRoot.Length).TrimStart('\')
    $destXbf = Join-Path $XamlDestRoot $rel
    $destDir = Split-Path $destXbf -Parent
    if (!(Test-Path $destDir)) { New-Item $destDir -ItemType Directory -Force | Out-Null }
    Copy-Item $f.FullName $destXbf -Force
}

$copiedCount = $xbfFiles.Count
if ($copiedCount -eq 0) {
    throw "No XBF files were copied from $XbfSourceRoot"
}
Write-Host "Copied $copiedCount XBF files into $AssemblyName/"

# resources.pri is NOT generated here.
# For WindowsPackageType=None self-contained unpackaged apps, WinUI3 resolves
# ms-appx:/// URIs directly from the file system (XAML files in the exe directory).
# A custom PRI with the wrong package name (e.g. "Application") causes
# STATUS_UNHANDLED_EXCEPTION in Microsoft.UI.Xaml.dll at startup.

Write-Host "Frontend publish OK: $AppPublishRoot"
