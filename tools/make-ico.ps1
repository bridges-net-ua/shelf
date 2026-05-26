param(
    [Parameter(Mandatory=$true)] [string]$SourcePng,
    [Parameter(Mandatory=$true)] [string]$OutIco,
    [int[]]$Sizes = @(16,24,32,48,64,128,256)
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng).Path)

$pngBlobs = @()
foreach ($size in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngBlobs += ,@{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}
$src.Dispose()

$count = $pngBlobs.Count
$headerSize = 6
$dirEntrySize = 16
$dataOffset = $headerSize + ($dirEntrySize * $count)

$out = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter $out

# ICONDIR
$bw.Write([uint16]0)        # reserved
$bw.Write([uint16]1)        # type = ICO
$bw.Write([uint16]$count)   # count

# ICONDIRENTRY x N
$cursor = $dataOffset
foreach ($blob in $pngBlobs) {
    $w = $blob.Size
    $h = $blob.Size
    $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))   # width  (0 = 256)
    $bw.Write([byte]($(if ($h -ge 256) { 0 } else { $h })))   # height (0 = 256)
    $bw.Write([byte]0)        # colour count
    $bw.Write([byte]0)        # reserved
    $bw.Write([uint16]1)      # planes
    $bw.Write([uint16]32)     # bpp
    $bw.Write([uint32]$blob.Bytes.Length)  # size
    $bw.Write([uint32]$cursor)             # offset
    $cursor += $blob.Bytes.Length
}

# Image data
foreach ($blob in $pngBlobs) {
    $bw.Write($blob.Bytes)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path -Parent $OutIco)).Path + '\' + (Split-Path -Leaf $OutIco), $out.ToArray())
$bw.Dispose()
$out.Dispose()

$info = Get-Item $OutIco
Write-Output ("OK: {0} ({1} bytes, {2} sizes)" -f $info.FullName, $info.Length, $count)
