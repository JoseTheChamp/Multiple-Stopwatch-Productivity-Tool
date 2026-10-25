# Draws the stopwatch app icon at several sizes and packs them into Assets\Stopwatch.ico.
param([string]$OutFile = (Join-Path $PSScriptRoot '..\Assets\Stopwatch.ico'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$body  = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)    # blue
$wedge = [System.Drawing.Color]::FromArgb(255, 191, 219, 254)  # light blue "elapsed" slice

$images = foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($size / 256.0, $size / 256.0)   # draw on a 256x256 grid

    $bodyBrush = New-Object System.Drawing.SolidBrush $body
    $wedgeBrush = New-Object System.Drawing.SolidBrush $wedge

    # Crown button and stem on top.
    $g.FillRectangle($bodyBrush, 100, 8, 56, 26)
    $g.FillRectangle($bodyBrush, 116, 26, 24, 26)

    # Small side button at 45 degrees.
    $state = $g.Save()
    $g.TranslateTransform(128, 150)
    $g.RotateTransform(45)
    $g.FillRectangle($bodyBrush, -14, -124, 28, 24)
    $g.Restore($state)

    # Case, face, elapsed slice, hand, hub.
    $g.FillEllipse($bodyBrush, 26, 48, 204, 204)
    $g.FillEllipse([System.Drawing.Brushes]::White, 48, 70, 160, 160)
    $g.FillPie($wedgeBrush, 60, 82, 136, 136, -90, 120)

    $pen = New-Object System.Drawing.Pen $body, 16
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $angle = 30 * [Math]::PI / 180
    $g.DrawLine($pen, 128, 150, 128 + 62 * [Math]::Cos($angle), 150 + 62 * [Math]::Sin($angle))
    $g.FillEllipse($bodyBrush, 113, 135, 30, 30)

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    if ($size -ge 128) {
        # Large sizes as PNG (standard for 256px icons).
        $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # Small sizes as classic 32-bit DIBs, which every Windows component can read.
        $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $pixels = New-Object byte[] ($data.Stride * $size)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
        $bmp.UnlockBits($data)

        $bw = New-Object System.IO.BinaryWriter $stream
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0)
        $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($row = $size - 1; $row -ge 0; $row--) { $bw.Write($pixels, $row * $data.Stride, $size * 4) }
        $maskRow = [int][Math]::Ceiling($size / 32.0) * 4
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    , $stream.ToArray()
}

# ICO container.
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
$file = [System.IO.File]::Create($OutFile)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($png in $images) { $writer.Write($png) }
$writer.Close()
"Wrote $OutFile"
