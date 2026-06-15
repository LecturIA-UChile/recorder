<#
.SYNOPSIS
    Builds the LecturIA application and produces the Windows installer.

.DESCRIPTION
    Runs three steps in order:
        1. dotnet publish self-contained, single-file (output: publish/win-x64).
        2. (optional) Sign the published executable with signtool when a
           certificate is supplied.
        3. Compile installer/LecturIA.iss with Inno Setup (output: dist/).
        4. (optional) Sign the resulting installer.

    Local signing is opt-in and only runs when a certificate is provided.
    Release builds are signed in CI through the SignPath Foundation
    integration; this script never reaches out to SignPath.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER SkipInstaller
    When set, the script stops after dotnet publish without running Inno Setup.

.PARAMETER CertificatePath
    Path to a .pfx certificate file used to sign the binaries. When omitted,
    signing falls back to -CertificateThumbprint; if neither is supplied,
    signing is skipped.

.PARAMETER CertificatePassword
    Password protecting the .pfx file referenced by -CertificatePath.

.PARAMETER CertificateThumbprint
    Thumbprint of a certificate already installed in the current user's
    certificate store. Mutually exclusive with -CertificatePath.

.PARAMETER TimestampUrl
    RFC 3161 timestamp authority. Defaults to http://timestamp.digicert.com.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -SkipInstaller
    ./build.ps1 -CertificatePath cert.pfx -CertificatePassword $env:CERT_PWD
    ./build.ps1 -CertificateThumbprint ABCDEF1234...
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $repoRoot

function Find-SignTool {
    $sdkRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { Test-Path $_ }

    foreach ($root in $sdkRoots) {
        $candidates = Get-ChildItem -Path $root -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "x64\\signtool\.exe$" } |
            Sort-Object -Property @{Expression = { $_.Directory.Parent.Name }; Descending = $true }
        if ($candidates) {
            return $candidates[0].FullName
        }
    }
    return $null
}

function Invoke-SignTool {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$FilePath
    )

    $hasCert = $CertificatePath -or $CertificateThumbprint
    if (-not $hasCert) {
        return $false
    }

    $signtool = Find-SignTool
    if (-not $signtool) {
        Write-Warning "Signing was requested but signtool.exe (Windows SDK) was not found."
        Write-Warning "Install it via: winget install Microsoft.WindowsSDK.10"
        return $false
    }

    $arguments = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")

    if ($CertificateThumbprint) {
        $arguments += @("/sha1", $CertificateThumbprint)
    }
    elseif ($CertificatePath) {
        $resolved = (Resolve-Path $CertificatePath).Path
        $arguments += @("/f", $resolved)
        if ($CertificatePassword) {
            $arguments += @("/p", $CertificatePassword)
        }
    }

    $arguments += $FilePath

    Write-Host "==> Signing $FilePath" -ForegroundColor Cyan
    & $signtool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "signtool.exe failed (exit code $LASTEXITCODE)."
    }
    return $true
}

Write-Host "==> Publishing LecturIA ($Configuration, win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish "src/LecturIA.App/LecturIA.App.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "publish/win-x64" `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit code $LASTEXITCODE)."
}

Get-ChildItem "publish/win-x64/*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

$exePath = "publish/win-x64/LecturIA.exe"
$signedExe = Invoke-SignTool -FilePath (Resolve-Path $exePath)
if (-not $signedExe -and ($CertificatePath -or $CertificateThumbprint)) {
    throw "Signing the executable failed."
}

if ($SkipInstaller) {
    Write-Host "==> Done. Executable: $exePath" -ForegroundColor Green
    return
}

Write-Host "==> Locating Inno Setup (ISCC.exe)..." -ForegroundColor Cyan
$isccCandidates = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe was not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup) and try again."
}

Write-Host "==> Compiling installer with $iscc" -ForegroundColor Cyan
& $iscc "installer/LecturIA.iss"
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed (exit code $LASTEXITCODE)."
}

$installer = Get-ChildItem "dist/LecturIA-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($installer) {
    [void](Invoke-SignTool -FilePath $installer.FullName)

    $sizeMb = [math]::Round($installer.Length / 1MB, 2)
    Write-Host "==> Installer ready: $($installer.FullName) ($sizeMb MB)" -ForegroundColor Green
}
