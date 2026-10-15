param(
    [string]$OutDir = 'C:\Users\LENOVO\projects\keyward\assets'
)

# Generates the wider Keyward brand kit: a horizontal wordmark and a social/hero banner,
# alongside the square logo produced by make-logo.ps1.
#
# Drawn here rather than exported from a design tool so the artwork stays in version control,
# carries no third-party licence, and cannot drift away from the in-app brand mark.

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$Accent  = [System.Drawing.Color]::FromArgb(255, 242, 179, 61)
$Accent2 = [System.Drawing.Color]::FromArgb(255, 255, 205, 110)
$Back    = [System.Drawing.Color]::FromArgb(255, 13, 16, 19)
$Back2   = [System.Drawing.Color]::FromArgb(255, 22, 28, 36)
$Text    = [System.Drawing.Color]::FromArgb(255, 233, 237, 244)
$Muted   = [System.Drawing.Color]::FromArgb(255, 155, 167, 184)

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($w, $h)),
        $Back, $Back2)
    $g.FillRectangle($brush, 0, 0, $w, $h)

    # Soft brass glow behind the mark. Built from concentric translucent ellipses rather than a
    # gradient brush: PowerShell cannot disambiguate LinearGradientBrush's 4-argument overloads,
    # and layered ellipses give the same smooth falloff.
    $glowSize = [int]([Math]::Max($w, $h) * 0.8)
    $glowCx   = [int]($glowSize * 0.14)
    $glowCy   = [int]($glowSize * 0.12)
    $layers   = 7
    for ($i = 0; $i -lt $layers; $i++) {
        $t = $i / [double]($layers - 1)          # 0 = centre, 1 = edge
        $diameter = [int]($glowSize * (1 - ($t * 0.62)))
        $alpha = [int](7 * (1 - $t))
        $col = [System.Drawing.Color]::FromArgb($alpha, $Accent.R, $Accent.G, $Accent.B)
        $b = New-Object System.Drawing.SolidBrush($col)
        $g.FillEllipse($b, ($glowCx - [int]($diameter / 2)), ($glowCy - [int]($diameter / 2)), $diameter, $diameter)
        $b.Dispose()
    }

    return @($bmp, $g)
}

# The vault mark, drawn centred in a square of side $s at ($x, $y). Matches Icons.cs in the app.
function Draw-Mark([System.Drawing.Graphics]$g, [int]$x, [int]$y, [int]$s) {
    $pen = New-Object System.Drawing.Pen($Accent, ($s * 0.052))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $r = $s * 0.088
    $dx = $x + ($s * 0.235); $dy = $y + ($s * 0.30)
    $dw = $s * 0.53;       $dh = $s * 0.50

    $door = New-Object System.Drawing.Drawing2D.GraphicsPath
    $door.AddArc($dx, $dy, $r * 2, $r * 2, 180, 90)
    $door.AddArc($dx + $dw - $r * 2, $dy, $r * 2, $r * 2, 270, 90)
    $door.AddArc($dx + $dw - $r * 2, $dy + $dh - $r * 2, $r * 2, $r * 2, 0, 90)
    $door.AddArc($dx, $dy + $dh - $r * 2, $r * 2, $r * 2, 90, 90)
    $door.CloseFigure()
    $g.DrawPath($pen, $door)

    $g.DrawArc($pen, ($x + $s * 0.348), ($y + $s * 0.16), ($s * 0.304), ($s * 0.304), 180, 180)

    $dialR = $s * 0.101
    $cx = $x + $s * 0.5; $cy = $y + $s * 0.547
    $g.DrawEllipse($pen, ($cx - $dialR), ($cy - $dialR), $dialR * 2, $dialR * 2)

    $spoke = New-Object System.Drawing.Pen($Accent2, ($s * 0.040))
    $spoke.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $spoke.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($spoke, $cx, $cy, $cx, ($cy - $dialR))
}

# -------------------------------------------------------------------------------------------
# 1. Horizontal wordmark:  mark + "Keyward"
# -------------------------------------------------------------------------------------------
$W = 1100; $H = 300
$canvas = New-Canvas $W $H
$bmp = $canvas[0]; $g = $canvas[1]

Draw-Mark $g 70 62 176

$font = New-Object System.Drawing.Font('Segoe UI Light', 112, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('Keyward', $font, (New-Object System.Drawing.SolidBrush($Text)), 290, 84)

$g.Dispose(); $bmp.Save((Join-Path $OutDir 'wordmark.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Host '  wordmark.png'

# -------------------------------------------------------------------------------------------
# 2. Hero / social banner:  mark + wordmark + tagline
# -------------------------------------------------------------------------------------------
$W = 1600; $H = 480
$canvas = New-Canvas $W $H
$bmp = $canvas[0]; $g = $canvas[1]

Draw-Mark $g 108 120 240

$font = New-Object System.Drawing.Font('Segoe UI Light', 118, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('Keyward', $font, (New-Object System.Drawing.SolidBrush($Text)), 412, 104)

# Sits below the wordmark's descender, not on top of it.
$tag = New-Object System.Drawing.Font('Segoe UI', 32, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('Encrypted on your machine.  No account.  No sync.  No telemetry.', $tag,
    (New-Object System.Drawing.SolidBrush($Muted)), 418, 296)

# Thin brass rule under the tagline.
$rule = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(110, $Accent), 2)
$g.DrawLine($rule, 420, 368, 1420, 368)

$g.Dispose(); $bmp.Save((Join-Path $OutDir 'banner.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Host '  banner.png'

# -------------------------------------------------------------------------------------------
# 3. Square mark on transparent, for the README header
# -------------------------------------------------------------------------------------------
$bmp = New-Object System.Drawing.Bitmap(512, 512)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
Draw-Mark $g 0 0 512
$g.Dispose(); $bmp.Save((Join-Path $OutDir 'brandmark.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Host '  brandmark.png'

Write-Host ''
Write-Host ("Brand kit written to {0}" -f $OutDir)
Get-ChildItem $OutDir | ForEach-Object { Write-Host ("    {0,-16} {1,9:N0} bytes" -f $_.Name, $_.Length) }
