$ErrorActionPreference = "Stop"

$BuildWorksRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $BuildWorksRoot
$FrontendProjectPath = Join-Path $RepoRoot "app\Alpheratz.Frontend.csproj"
$AppPublishRoot = Join-Path $BuildWorksRoot "artifacts\publish\Alpheratz"
$XbfSourceRoot = Join-Path $RepoRoot "app\artifacts\obj\x64\Release\net8.0-windows10.0.19041.0\win-x64"

if (Test-Path $AppPublishRoot) {
    Remove-Item -LiteralPath $AppPublishRoot -Recurse -Force
}

# self-contained の Frontend 配布物を生成する。
# project 設定に依存せず、CLI 引数でも self-contained を明示する。
& dotnet publish $FrontendProjectPath -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $AppPublishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Frontend の publish に失敗しました。終了コード: $LASTEXITCODE"
}

$FrontendExe = Join-Path $AppPublishRoot "Alpheratz.Frontend.exe"
if (!(Test-Path $FrontendExe)) {
    throw "Missing frontend executable: $FrontendExe"
}

# WinExe project の dotnet publish は XBF/XAML をコピーしない。
# unpackaged WinUI 3 は ms-appx:/// URI を filesystem から解決する。生成された
# LoadComponent は root 相対の ms-appx:/// を使うため、XBF を publish root へ配置する。
# 旧出力が参照する assembly 名の subdirectory にも互換用コピーを残す。
# 対象は ms-appx:///{AssemblyName}/ を参照する旧生成コードである。
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

# 独自の app PRI は生成しない。アプリの XAML は個別配置した XBF から解決するが、
# XamlControlsResources は Microsoft.UI.Xaml の テーマ XBF を、
# この unpackaged 配置では app root の resources.pri 名を介して解決する。
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
