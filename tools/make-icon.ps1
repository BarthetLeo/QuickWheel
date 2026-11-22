# Génère Assets\QuickWheel.ico et Assets\logo.png (même dessin que Views\Logo.cs).
# Usage : powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'

$surface = [System.Drawing.Color]::FromArgb(255, 0x16, 0x18, 0x1D)
$line    = [System.Drawing.Color]::FromArgb(255, 0x2B, 0x30, 0x3A)
$brass   = [System.Drawing.Color]::FromArgb(255, 0xC9, 0xA8, 0x6A)
$dim     = [System.Drawing.Color]::FromArgb(255, 0x5E, 0x54, 0x40)

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 100.0
    $g.ScaleTransform($k, $k)

    # Sans fond : 6 cases (celle du haut en laiton) et le moyeu, sur transparent.
    # La roue occupe presque tout le carré pour rester lisible en 16 px.
    $inner = 27; $outer = 49; $gap = 9; $step = 60
    for ($i = 0; $i -lt 6; $i++) {
        $start = $i * $step - $step / 2 + $gap / 2 - 90
        $sweep = $step - $gap
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddArc(50 - $outer, 50 - $outer, $outer * 2, $outer * 2, $start, $sweep)
        $p.AddArc(50 - $inner, 50 - $inner, $inner * 2, $inner * 2, $start + $sweep, -$sweep)
        $p.CloseFigure()
        $c = if ($i -eq 0) { $brass } else { $dim }
        $g.FillPath((New-Object System.Drawing.SolidBrush $c), $p)
    }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $brass), 37, 37, 26, 26)
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    # Entrée d'icône au format BMP 32 bits (meilleure compatibilité que PNG pour les petites tailles)
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s
    if ($s -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += , @($s, $ms.ToArray())
        $bmp.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        $images += , @($s, (Get-DibBytes $bmp))
    }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $data = $img[1]
    $dim8 = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim8); $w.Write([byte]$dim8); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'QuickWheel.ico'), $out.ToArray())
Write-Output "OK : $(Join-Path $assets 'QuickWheel.ico')"
