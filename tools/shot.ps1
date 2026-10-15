param(
    [string]$Title = 'Keyward',
    [string]$Out  = 'C:\Users\LENOVO\projects\keyward\artifacts\shot.png',
    [switch]$NoRaise
)

# Full-physical-screen capture with the target window held on top for the duration.
#
# Two things this works around, both found the hard way on this machine:
#   * The real display is 1920x1080 but every DPI-unaware API (SystemInformation.VirtualScreen,
#     GetWindowRect) reports a virtualised 1280x720. CopyFromScreen works in physical pixels, so
#     the capture size is passed explicitly rather than taken from those APIs.
#   * SetForegroundWindow is blocked by the foreground lock, so the window is raised with
#     SetWindowPos(HWND_TOPMOST) instead and only demoted after the bitmap is taken.

Add-Type -AssemblyName System.Drawing

$sig = @'
using System;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    public static void Top(IntPtr h)    { ShowWindow(h, 9); SetWindowPos(h, new IntPtr(-1), 0,0,0,0, 0x0002|0x0001|0x0010); SetForegroundWindow(h); }
    public static void Untop(IntPtr h) { SetWindowPos(h, new IntPtr(-2), 0,0,0,0, 0x0002|0x0001|0x0010); }
}
'@
if (-not ('W' -as [type])) { Add-Type -TypeDefinition $sig }

$p = Get-Process | Where-Object { $_.MainWindowTitle -like "*$Title*" } | Select-Object -First 1
if (-not $p) { Write-Output "window '$Title' not found"; exit 1 }

if (-not $NoRaise) { [W]::Top($p.MainWindowHandle) }
Start-Sleep -Milliseconds 1100

$bmp = New-Object System.Drawing.Bitmap(1920, 1080)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

if (-not $NoRaise) { [W]::Untop($p.MainWindowHandle) }
Write-Output ("captured -> {0}" -f $Out)
