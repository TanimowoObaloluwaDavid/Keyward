param(
    [string]$OutDir = 'C:\Users\LENOVO\projects\keyward\assets'
)

# Generates the Keyward logo and Windows icon from the same design as the in-app brand mark:
# a rounded vault door with a shackle and a dial, in brass on the near-black app background.
#
# Drawn here rather than exported from a design tool so the icon and the application cannot
# drift apart, and so the repository carries no third-party artwork licence.

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$Accent  = [System.Drawing.Color]::FromArgb(255, 242, 179, 61)   # #F2B33D brass
$Accent2 = [System.Drawing.Color]::FromArgb(255, 255, 205, 110)  # lighter brass, for depth
$Back    = [System.Drawing.Color]::FromArgb(255, 13, 16, 19)     # #0D1013

function New-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 512.0

    # ---- rounded background plate ------------------------------------------------------
    $radius = 108 * $s
    $plate = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $plate.AddArc(0, 0, $d, $d, 180, 90)
    $plate.AddArc($size - $d, 0, $d, $d, 270, 90)
    $plate.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $plate.AddArc(0, $size - $d, $d, $d, 90, 90)
    $plate.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($size, $size)),
        $Back, ([System.Drawing.Color]::FromArgb(255, 26, 32, 40)))
    $g.FillPath($brush, $plate)

    # ---- vault mark ---------------------------------------------------------------------
    $pen = New-Object System.Drawing.Pen($Accent, (26 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin     = [System.Drawing.Drawing2D.LineJoin]::Round

    # door
    $door = New-Object System.Drawing.Drawing2D.GraphicsPath
    $dr = 44 * $s
    $dx = 118 * $s; $dy = 150 * $s; $dw = 276 * $s; $dh = 260 * $s
    $door.AddArc($dx, $dy, $dr * 2, $dr * 2, 180, 90)
    $door.AddArc($dx + $dw - $dr * 2, $dy, $dr * 2, $dr * 2, 270, 90)
    $door.AddArc($dx + $dw - $dr * 2, $dy + $dh - $dr * 2, $dr * 2, $dr * 2, 0, 90)
    $door.AddArc($dx, $dy + $dh - $dr * 2, $dr * 2, $dr * 2, 90, 90)
    $door.CloseFigure()
    $g.DrawPath($pen, $door)

    # shackle
    $shacklePen = New-Object System.Drawing.Pen($Accent, (26 * $s))
    $shacklePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $shacklePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($shacklePen, 178 * $s, 82 * $s, 156 * $s, 156 * $s, 180, 180)

    # dial
    $dialR = 52 * $s
    $cx = 256 * $s; $cy = 280 * $s
    $g.DrawEllipse($pen, ($cx - $dialR), ($cy - $dialR), $dialR * 2, $dialR * 2)

    # dial handle
    $spokePen = New-Object System.Drawing.Pen($Accent2, (20 * $s))
    $spokePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $spokePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($spokePen, $cx, $cy, $cx, $cy - $dialR)

    $g.Dispose()
    return $bmp
}

function Save-Png($bmp, [string]$path, [int]$size) {
    $out = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($bmp, 0, 0, $size, $size)
    $g.Dispose()
    $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
}

$master = New-Logo 1024

# ---- PNG assets -------------------------------------------------------------------------
Save-Png $master (Join-Path $OutDir 'logo.png') 512
Save-Png $master (Join-Path $OutDir 'logo-256.png') 256
$mark = New-Logo 512
Save-Png $mark (Join-Path $OutDir 'mark.png') 512
$mark.Dispose()

# ---- Multi-size Windows icon ------------------------------------------------------------
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $tmp = Join-Path $env:TEMP ("kw_icon_{0}.png" -f $s)
    Save-Png $master $tmp $s
    $pngs += ,@{ Size = $s; Data = [System.IO.File]::ReadAllBytes($tmp) }
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}

# ICO with PNG-compressed entries (understood by Windows Vista and later).
$icoPath = Join-Path $OutDir 'Keyward.ico'
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type = icon
$bw.Write([UInt16]$pngs.Count)    # image count

$offset = 6 + (16 * $pngs.Count)
foreach ($p in $pngs) {
    # ICO stores the dimension in a single byte, so 256 has to be written as 0.
    $dim = 0
    if ($p.Size -lt 256) { $dim = $p.Size }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)             # palette
    $bw.Write([Byte]0)             # reserved
    $bw.Write([UInt16]1)           # colour planes
    $bw.Write([UInt16]32)          # bits per pixel
    $bw.Write([UInt32]$p.Data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $p.Data.Length
}
foreach ($p in $pngs) { $bw.Write($p.Data) }

$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
$master.Dispose()

Write-Host ''
Write-Host '  Generated:' -ForegroundColor Green
foreach ($f in @('logo.png', 'logo-256.png', 'mark.png', 'Keyward.ico')) {
    $path = Join-Path $OutDir $f
    $len = (Get-Item $path).Length
    Write-Host ("    {0,-14} {1,9:N0} bytes" -f $f, $len)
}
