param(
    [string]$Artifacts = 'C:\Users\LENOVO\projects\keyward\artifacts',
    [string]$OutDir    = 'C:\Users\LENOVO\projects\keyward\docs\images'
)

# Crops full-desktop captures down to just the application window, so the README shows the
# product rather than whatever else was on screen at the time.
#
# The captures are physical 1920x1080. The window sits at virtualised (50,24) sized 1180x672,
# and this process is DPI-unaware while the app is per-monitor aware, so the conversion factor
# (1.5 on this 150% display) has to be applied explicitly.

Add-Type -AssemblyName System.Drawing

$scale = 1.5
$virtualLeft = 50
$virtualTop  = 24
$virtualW    = 1180
$virtualH    = 672

# A couple of virtual pixels in, so the desktop behind the window is not visible at the edges.
$x = [int](($virtualLeft + 1) * $scale)
$y = [int]($virtualTop * $scale)
$w = [int]($virtualW * $scale)
$h = [int]($virtualH * $scale)

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$jobs = @(
    @{ In = 'v10.png';           Out = 'first-run.png' },
    @{ In = 'installed2.png';    Out = 'unlock.png' },
    @{ In = 'drive-1-filled.png';Out = 'strength.png' },
    @{ In = 'drive.png';         Out = 'vault.png' },
    @{ In = 'generator.png';     Out = 'generator.png' }
)

foreach ($j in $jobs) {
    $src = Join-Path $Artifacts $j.In
    if (-not (Test-Path $src)) { Write-Host "  skip  $($j.In) (not found)"; continue }

    $img = [System.Drawing.Bitmap]::FromFile($src)

    # Clamp to the source so a stale capture cannot throw.
    $cw = [Math]::Min($w, $img.Width - $x)
    $ch = [Math]::Min($h, $img.Height - $y)

    $rect = New-Object System.Drawing.Rectangle($x, $y, $cw, $ch)
    $crop = $img.Clone($rect, $img.PixelFormat)

    $dest = Join-Path $OutDir $j.Out
    $crop.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)

    Write-Host ("  {0,-16} {1,5} x {2,-5} -> {3}" -f $j.In, $cw, $ch, $j.Out)

    $crop.Dispose()
    $img.Dispose()
}

Write-Host ''
Write-Host ("Images written to {0}" -f $OutDir)
