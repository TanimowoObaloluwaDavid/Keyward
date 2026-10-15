param(
    [string]$Out = 'C:\Users\LENOVO\projects\keyward\artifacts\drive.png'
)

# Drives the real UI with synthetic input so the create-vault -> workspace flow is exercised
# end to end, rather than assumed to work because it compiled.

Add-Type -AssemblyName System.Drawing, System.Windows.Forms

$sig = @'
using System;
using System.Runtime.InteropServices;
public static class M {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a,int x,int y,int cx,int cy,uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);

    public static void Top(IntPtr h) {
        ShowWindow(h, 9);
        SetWindowPos(h, new IntPtr(-1), 0,0,0,0, 0x0002|0x0001|0x0010);
        SetForegroundWindow(h);
    }
    public static void Untop(IntPtr h) { SetWindowPos(h, new IntPtr(-2), 0,0,0,0, 0x0002|0x0001|0x0010); }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);   // LEFTDOWN
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);   // LEFTUP
    }
}
'@
if (-not ('M' -as [type])) { Add-Type -TypeDefinition $sig }

function Shot($path) {
    $bmp = New-Object System.Drawing.Bitmap(1920, 1080)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$p = Get-Process | Where-Object { $_.MainWindowTitle -like '*Keyward*' } | Select-Object -First 1
if (-not $p) { Write-Output 'Keyward not running'; exit 1 }

[M]::Top($p.MainWindowHandle)
Start-Sleep -Milliseconds 900

# Coordinates are in PHYSICAL screen pixels (read off a full-resolution capture) and are divided
# by the display scale before being handed to the OS. This process is DPI-unaware, so
# SetCursorPos interprets its arguments as virtualised coordinates, while the app is per-monitor
# DPI aware. Without the conversion every click lands at 1.5x the intended spot.
$scale = 1.5
function V([double]$x, [double]$y) { [int]($x / $scale), [int]($y / $scale) }

# Master password field, then the confirm field, then the Create vault button.
# Click the left-hand text area, not the middle of the field: SecretInput puts a reveal button in
# the right-hand column and the field is stretched, so a centre click lands on the button.
$cx, $cy = V 750 517
[M]::Click($cx, $cy)
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('correct-horse-battery-staple')

Start-Sleep -Milliseconds 300
$cx, $cy = V 750 671
[M]::Click($cx, $cy)
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('correct-horse-battery-staple')

Start-Sleep -Milliseconds 500
Shot 'C:\Users\LENOVO\projects\keyward\artifacts\drive-1-filled.png'

$cx, $cy = V 945 966
[M]::Click($cx, $cy)
Start-Sleep -Seconds 9   # PBKDF2 runs on the UI thread; give it room

Shot $Out

if (Test-Path "$env:APPDATA\Keyward\crash.log") {
    Write-Output 'CRASH LOG:'
    Get-Content "$env:APPDATA\Keyward\crash.log" | Select-Object -First 12
} else {
    Write-Output 'no crash'
}

[M]::Untop($p.MainWindowHandle)
Write-Output "done -> $Out"
