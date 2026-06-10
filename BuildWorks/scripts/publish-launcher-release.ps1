$ErrorActionPreference = "Stop"

$BuildWorksRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $BuildWorksRoot
$LauncherProjectPath = Join-Path $BuildWorksRoot "launcher\Alpheratz.Launcher.csproj"
$LauncherPublishRoot = Join-Path $RepoRoot "artifacts\publish\Launcher"

if (Test-Path $LauncherPublishRoot) {
    Remove-Item $LauncherPublishRoot -Recurse -Force
}

& dotnet publish $LauncherProjectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $LauncherPublishRoot

$LauncherExe = Join-Path $LauncherPublishRoot "Alpheratz.exe"
if (!(Test-Path $LauncherExe)) {
    throw "Missing launcher executable: $LauncherExe"
}

Write-Host "Launcher publish OK: $LauncherPublishRoot"
