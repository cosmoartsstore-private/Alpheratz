$ErrorActionPreference = "Stop"

$BuildWorksRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $BuildWorksRoot
$FrontendProjectPath = Join-Path $RepoRoot "app\Alpheratz.Frontend.csproj"
$AppPublishRoot = Join-Path $BuildWorksRoot "artifacts\publish\Alpheratz"
$XbfSourceRoot = Join-Path $RepoRoot "app\artifacts\obj\x64\Release\net8.0-windows10.0.19041.0\win-x64"

if (Test-Path $AppPublishRoot) {
    Remove-Item -LiteralPath $AppPublishRoot -Recurse -Force
}

# Build a self-contained frontend package.
# The CLI flag keeps the script explicit even when the project file already sets it.
& dotnet publish $FrontendProjectPath -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $AppPublishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Frontend の publish に失敗しました。終了コード: $LASTEXITCODE"
}

$FrontendExe = Join-Path $AppPublishRoot "Alpheratz.Frontend.exe"
if (!(Test-Path $FrontendExe)) {
    throw "Missing frontend executable: $FrontendExe"
}

# dotnet publish does not copy XBF/XAML files for WinExe projects.
# WinUI3 unpackaged apps resolve ms-appx:/// URIs from the file system. Generated
# LoadComponent calls use root-relative ms-appx:/// paths, so mirror XBF files
# into the publish root. Keep the assembly-name subdirectory as a compatibility
# copy for older outputs that referenced ms-appx:///{AssemblyName}/ paths.
$AssemblyName = "Alpheratz.Frontend"
$rootCopiedCount = 0
$xbfFiles = Get-ChildItem $XbfSourceRoot -Recurse -Filter "*.xbf"
foreach ($f in $xbfFiles) {
    $rel = $f.FullName.Substring($XbfSourceRoot.Length).TrimStart('\')
    $destXbf = Join-Path $AppPublishRoot $rel
    $destDir = Split-Path $destXbf -Parent
    if (!(Test-Path $destDir)) { New-Item $destDir -ItemType Directory -Force | Out-Null }
    Copy-Item $f.FullName $destXbf -Force
    $rootCopiedCount++
}

$XamlDestRoot = Join-Path $AppPublishRoot $AssemblyName
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
if ($rootCopiedCount -ne $copiedCount) {
    throw "Publish root XBF copy count mismatch. Root=$rootCopiedCount; Assembly=$copiedCount"
}
Write-Host "Copied $rootCopiedCount XBF files into publish root"
Write-Host "Copied $copiedCount XBF files into $AssemblyName/"

$ControlsPri = Join-Path $AppPublishRoot "Microsoft.UI.Xaml.Controls.pri"
if (!(Test-Path $ControlsPri)) {
    throw "Missing WinUI controls PRI: $ControlsPri"
}

# Do not generate a custom app PRI here. The app XAML is resolved from loose XBF
# files, but XamlControlsResources looks up Microsoft.UI.Xaml theme XBF through
# the app root resources.pri name in this unpackaged layout.
$AppPri = Join-Path $AppPublishRoot "resources.pri"
Copy-Item $ControlsPri $AppPri -Force
Write-Host "Copied Microsoft.UI.Xaml.Controls.pri to resources.pri"

$requiredRuntimeFiles = @(
    "App.xbf",
    "MainWindow.xbf",
    "resources.pri"
)
foreach ($requiredFile in $requiredRuntimeFiles) {
    $requiredPath = Join-Path $AppPublishRoot $requiredFile
    if (!(Test-Path $requiredPath)) {
        throw "Missing required XAML runtime file: $requiredPath"
    }
}

Write-Host "Frontend publish OK: $AppPublishRoot"
