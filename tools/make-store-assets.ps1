# Generates Microsoft Store-required PNG assets from Resources/shelf.png.
#
# Pure .NET (System.Drawing) — does NOT require Windows SDK. Run this any time
# the source logo changes:
#   pwsh tools\make-store-assets.ps1
#
# Output: 5 PNG files in Shelf.Package\Assets\ referenced from Package.appxmanifest.

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Source = Join-Path $ProjectRoot "Resources\shelf.png"
$OutDir = Join-Path $ProjectRoot "Shelf.Package\Assets"

if (-not (Test-Path $Source)) {
    Write-Error "Source logo not found: $Source"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile($Source)

function Save-Square {
    param([int]$Size, [string]$FileName)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.DrawImage($src, 0, 0, $Size, $Size)
    $g.Dispose()
    $path = Join-Path $OutDir $FileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  -> $FileName ($Size x $Size)"
}

function Save-CenteredOnCanvas {
    param([int]$CanvasW, [int]$CanvasH, [int]$LogoSize, [string]$FileName)

    $bmp = New-Object System.Drawing.Bitmap $CanvasW, $CanvasH
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $x = [int](($CanvasW - $LogoSize) / 2)
    $y = [int](($CanvasH - $LogoSize) / 2)
    $g.DrawImage($src, $x, $y, $LogoSize, $LogoSize)
    $g.Dispose()
    $path = Join-Path $OutDir $FileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  -> $FileName ($CanvasW x $CanvasH, logo $LogoSize px)"
}

Write-Host "Source: $Source ($($src.Width) x $($src.Height))"
Write-Host "Output: $OutDir"
Write-Host ""

# Square tiles + taskbar icon + Store listing logo.
Save-Square -Size 44 -FileName "Square44x44Logo.png"
Save-Square -Size 150 -FileName "Square150x150Logo.png"
Save-Square -Size 50 -FileName "StoreLogo.png"

# Wide tile and splash screen — non-square canvases with centered square logo.
# Logo size leaves breathing room at the canvas edges.
Save-CenteredOnCanvas -CanvasW 310 -CanvasH 150 -LogoSize 130 -FileName "Wide310x150Logo.png"
Save-CenteredOnCanvas -CanvasW 620 -CanvasH 300 -LogoSize 260 -FileName "SplashScreen.png"

$src.Dispose()

Write-Host ""
Write-Host "Done. 5 PNG files in $OutDir"
