<#
.SYNOPSIS
  Draws the SPLAT! for Windows icon (gui\SplatWin.App\splat.ico): a radio mast sending coverage
  rings in the colours of SPLAT!'s signal key, on a dark blue tile. Original artwork for this
  project (GPLv2 with the app). Run from Windows PowerShell 5.1; uses System.Drawing only.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make_icon.ps1
#>
param([string]$Out = (Join-Path $PSScriptRoot '..\gui\SplatWin.App\splat.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    # Rounded tile
    $r = 48 * $s; $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r; $w = $size - 1
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 11, 61, 107))), $path)
    # Coverage rings around the mast top (SPLAT! key colours: red strongest, then yellow, green, blue)
    $cx = 128 * $s; $cy = 104 * $s
    $rings = @(@(150, 0, 148, 255), @(118, 0, 208, 0), @(86, 255, 206, 0), @(54, 255, 0, 0))   # strong (red) near the mast, as in the key
    $penW = [Math]::Max(1.5, 16 * $s)
    if ($size -le 24) { $rings = @(@(110, 0, 208, 0), @(62, 255, 0, 0)); $penW = [Math]::Max(1.5, 22 * $s) }
    foreach ($ring in $rings) {
        $rr = $ring[0] * $s
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, $ring[1], $ring[2], $ring[3])), $penW
        $g.DrawArc($pen, [single]($cx - $rr / 2), [single]($cy - $rr / 2), [single]$rr, [single]$rr, 200, 140)
        $g.DrawArc($pen, [single]($cx - $rr / 2), [single]($cy - $rr / 2), [single]$rr, [single]$rr, 20, 140)
        $pen.Dispose()
    }
    # The mast: a white tapering tower from the ground to the antenna
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pts = @(
        (New-Object System.Drawing.PointF ([single](128 * $s), [single](96 * $s))),
        (New-Object System.Drawing.PointF ([single](162 * $s), [single](226 * $s))),
        (New-Object System.Drawing.PointF ([single](94 * $s), [single](226 * $s))))
    $g.FillPolygon($white, $pts)
    $navy = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 11, 61, 107))
    if ($size -ge 32) {   # lattice gaps, only where they can be seen
        $g.FillPolygon($navy, @(
            (New-Object System.Drawing.PointF ([single](128 * $s), [single](140 * $s))),
            (New-Object System.Drawing.PointF ([single](145 * $s), [single](208 * $s))),
            (New-Object System.Drawing.PointF ([single](111 * $s), [single](208 * $s)))))
    }
    $g.FillEllipse($white, [single](114 * $s), [single](90 * $s), [single](28 * $s), [single](28 * $s))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($z in $sizes) { ,(New-IconPng $z) }
# ICO: header, one 16-byte entry per image, then the PNG data (PNG-compressed entries, Vista and later).
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($z % 256)); $bw.Write([byte]($z % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $ms.ToArray())
# Check: WPF (what the app uses) reads back every size. (System.Drawing.Icon can't read PNG entries.)
Add-Type -AssemblyName PresentationCore
$fs = [System.IO.File]::OpenRead($Out)
try {
    $dec = New-Object System.Windows.Media.Imaging.IconBitmapDecoder $fs, ([System.Windows.Media.Imaging.BitmapCreateOptions]::None), ([System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $got = @($dec.Frames | ForEach-Object { $_.PixelWidth })
} finally { $fs.Dispose() }
if (($got -join ',') -ne ($sizes -join ',')) { throw "icon check failed: read back sizes $($got -join ',')" }
Write-Host "Wrote $Out ($($sizes.Count) sizes, $((Get-Item $Out).Length) bytes)"
