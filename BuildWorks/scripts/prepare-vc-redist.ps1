param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,

    [switch]$UseCached
)

$ErrorActionPreference = "Stop"

$DownloadUri = "https://aka.ms/vc14/vc_redist.x64.exe"
$DestinationPath = [System.IO.Path]::GetFullPath($DestinationPath)
$DestinationDirectory = Split-Path -Parent $DestinationPath
$DownloadPath = Join-Path $DestinationDirectory "vc_redist.x64.download"

function Test-MicrosoftSignedExecutable {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    try {
        $signature = Get-AuthenticodeSignature -LiteralPath $Path
        $versionInfo = (Get-Item -LiteralPath $Path).VersionInfo
        $hasExpectedIdentity = $versionInfo.OriginalFilename -ieq "VC_redist.x64.exe" `
            -and $versionInfo.CompanyName -eq "Microsoft Corporation" `
            -and $versionInfo.ProductName -like "Microsoft Visual C++*Redistributable*(x64)*"

        return $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid `
            -and $null -ne $signature.SignerCertificate `
            -and $signature.SignerCertificate.Subject -match "CN=Microsoft Corporation(?:,|$)" `
            -and $hasExpectedIdentity
    }
    catch {
        return $false
    }
}

$hasValidCache = Test-MicrosoftSignedExecutable -Path $DestinationPath
if ($UseCached -and $hasValidCache) {
    $cachedVersion = (Get-Item -LiteralPath $DestinationPath).VersionInfo.ProductVersion
    Write-Host "Using cached Microsoft Visual C++ Redistributable: $cachedVersion"
    return
}

if (!(Test-Path -LiteralPath $DestinationDirectory)) {
    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
}

if (Test-Path -LiteralPath $DownloadPath) {
    Remove-Item -LiteralPath $DownloadPath -Force
}

try {
    Write-Host "Downloading Microsoft Visual C++ Redistributable (x64)"
    Invoke-WebRequest -Uri $DownloadUri -OutFile $DownloadPath -UseBasicParsing

    if (!(Test-MicrosoftSignedExecutable -Path $DownloadPath)) {
        throw "Downloaded Visual C++ Redistributable does not have a valid Microsoft signature."
    }

    Move-Item -LiteralPath $DownloadPath -Destination $DestinationPath -Force
}
catch {
    if ($hasValidCache) {
        Write-Warning "Visual C++ Redistributable download failed. Reusing the valid cached package. $($_.Exception.Message)"
        return
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $DownloadPath) {
        Remove-Item -LiteralPath $DownloadPath -Force
    }
}

$downloadedVersion = (Get-Item -LiteralPath $DestinationPath).VersionInfo.ProductVersion
Write-Host "Prepared Microsoft Visual C++ Redistributable: $downloadedVersion"
