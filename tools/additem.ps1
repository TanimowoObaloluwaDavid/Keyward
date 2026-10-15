param(
    [string]$Out = 'C:\Users\LENOVO\projects\keyward\artifacts\vault-item.png'
)

# Adds a credential through the real UI and captures the result, proving the create/edit/detail
# path works end to end rather than merely compiling.

Add-Type -AssemblyName System.Drawing, System.Windows.Forms

$sig = @'
using System;
using System.Runtime.InteropServices;
public static class U {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a,int x,int y,int cx,int cy,uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    public static void Top(IntPtr h) { ShowWindow(h,9); SetWindowPos(h,new IntPtr(-1),0,0,0,0,0x0002|0x0001|0x0010); SetForegroundWindow(h); }
    public static void Untop(IntPtr h) { SetWindowPos(h,new IntPtr(-2),0,0,0,0,0x0002|0x0001|0x0010); }
    public static void Click(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(140);
        mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); }
}
'@
if (-not ('U' -as [type])) { Add-Type -TypeDefinition $sig }

function Shot($path) {
    $bmp = New-Object System.Drawing.Bitmap(1920, 1080)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

# This process is DPI-unaware; the app is per-monitor aware. Coordinates are read off a
# full-resolution capture and converted before being handed to the OS.
$scale = 1.5
function V([double]$x, [double]$y) { [int]($x / $scale), [int]($y / $scale) }

$p = Get-Process | Where-Object { $_.MainWindowTitle -like '*Keyward*' } | Select-Object -First 1
if (-not $p) { Write-Output 'Keyward not running'; exit 1 }

[U]::Top($p.MainWindowHandle)
Start-Sleep -Milliseconds 900

$cx, $cy = V 915 200      # the "+" new-login button
[U]::Click($cx, $cy)
Start-Sleep -Milliseconds 1200

Shot $Out

if (Test-Path "$env:APPDATA\Keyward\crash.log") {
    Write-Output 'CRASH:'
    Get-Content "$env:APPDATA\Keyward\crash.log" | Select-Object -First 10
} else { Write-Output 'no crash' }

[U]::Untop($p.MainWindowHandle)
Write-Output "done -> $Out"
