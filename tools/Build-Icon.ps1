<#
.SYNOPSIS
    Regenerates src/LecturIA.App/Assets/app.ico from icon.png.

.DESCRIPTION
    Converts the source PNG into a single-resolution ICO file suitable for
    WPF and Inno Setup. Run this script only when the logo design changes;
    the resulting .ico is committed to the repository.

.PARAMETER SourcePng
    Path to the source PNG file. Defaults to the canonical location under
    src/LecturIA.App/Assets/.

.PARAMETER OutIco
    Path of the .ico file to generate. Defaults to the canonical location
    under src/LecturIA.App/Assets/.

.NOTES
    The generated .ico embeds a single 256x256 frame, which is enough for
    the installer, the application window, and the taskbar. If multiple
    resolutions are required, switch to ImageMagick.
#>

[CmdletBinding()]
param(
    [string]$SourcePng = "src/LecturIA.App/Assets/icon.png",
    [string]$OutIco    = "src/LecturIA.App/Assets/app.ico"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
Set-Location $repoRoot

if (-not (Test-Path $SourcePng)) {
    throw "Source file not found: $SourcePng"
}

Add-Type -AssemblyName System.Drawing
$png = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng))
try {
    $bitmap = New-Object System.Drawing.Bitmap $png, 256, 256
    try {
        $hicon = $bitmap.GetHicon()
        $icon  = [System.Drawing.Icon]::FromHandle($hicon)
        $fs    = [System.IO.File]::Create((Join-Path (Get-Location) $OutIco))
        try {
            $icon.Save($fs)
        }
        finally {
            $fs.Close()
            $icon.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}
finally {
    $png.Dispose()
}

$size = [math]::Round((Get-Item $OutIco).Length / 1KB, 1)
Write-Host "Generated: $OutIco ($size KB)" -ForegroundColor Green
