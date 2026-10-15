<#
    Installs Keyward for the current user.

        .\install.ps1                    install and launch
        .\install.ps1 -NoLaunch          install only
        .\install.ps1 -DesktopShortcut   also put a shortcut on the desktop

    Installs to %LOCALAPPDATA%\Programs\Keyward by default, which needs no elevation and is
    removable by deleting the folder plus the Start Menu entry.

    This is a plain file copy, not an MSI: the target machine has no installer toolchain
    (no WiX, no MSIX packaging, no signing certificate), and a signed installer that cannot be
    signed would be worse than an honest script.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Keyward",
    [switch]$DesktopShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$Root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
$Artifacts = Join-Path $Root 'artifacts'
$StartMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'

function Write-Step($Text) { Write-Host "  $Text" }

Write-Host ''
Write-Host 'Installing Keyward' -ForegroundColor Cyan
Write-Host "  target : $InstallDir"
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 1. Build Release
# ---------------------------------------------------------------------------------------------
Write-Step 'Building (Release) ...'

Get-Process -Name 'Keyward' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

& (Join-Path $Root 'build.ps1') -Configuration Release | Out-Null

$Exe = Join-Path $Artifacts 'Keyward.exe'
$Core = Join-Path $Artifacts 'Keyward.Core.dll'
foreach ($f in @($Exe, $Core)) {
    if (-not (Test-Path $f)) { throw "Build did not produce $f" }
}

# ---------------------------------------------------------------------------------------------
# 2. Copy
# ---------------------------------------------------------------------------------------------
Write-Step 'Copying files ...'

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

foreach ($f in @($Exe, $Core)) {
    Copy-Item -LiteralPath $f -Destination (Join-Path $InstallDir (Split-Path -Leaf $f)) -Force
}

# The app manifest is already embedded in the exe at compile time (per-monitor DPI awareness),
# so nothing else is required at run time.

# ---------------------------------------------------------------------------------------------
# 3. Shortcuts
# ---------------------------------------------------------------------------------------------
Write-Step 'Creating shortcuts ...'

$shell = New-Object -ComObject WScript.Shell
$TargetExe = Join-Path $InstallDir 'Keyward.exe'
$StartLink = Join-Path $StartMenu 'Keyward.lnk'

function New-Shortcut($Path) {
    $s = $shell.CreateShortcut($Path)
    $s.TargetPath = $TargetExe
    $s.WorkingDirectory = $InstallDir
    $s.IconLocation = "$TargetExe,0"
    $s.Description = 'Keyward - local encrypted password manager'
    $s.Save()
}

New-Shortcut $StartLink
Write-Step "  Start Menu : $StartLink"

if ($DesktopShortcut) {
    $DesktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Keyward.lnk'
    New-Shortcut $DesktopLink
    Write-Step "  Desktop    : $DesktopLink"
}

# ---------------------------------------------------------------------------------------------
# 4. Launch
# ---------------------------------------------------------------------------------------------

$vault = Join-Path $env:APPDATA 'Keyward\vault.kvw'
if (Test-Path $vault) {
    Write-Step "Existing vault preserved at $vault"
} else {
    Write-Step 'No vault yet - one will be created on first run.'
}

if (-not $NoLaunch) {
    Write-Step 'Launching ...'
    $p = Start-Process $TargetExe -PassThru -ErrorAction SilentlyContinue
    if (-not $p) {
        # Retry once: the first launch of a freshly copied binary can be scanned and refused.
        Start-Sleep -Milliseconds 800
        $p = Start-Process $TargetExe -PassThru -ErrorAction SilentlyContinue
    }
    if ($p) {
        Start-Sleep -Seconds 4
        $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        if ($alive) {
            Write-Step "Running (pid $($p.Id))." -ForegroundColor Green
        } else {
            Write-Host '  The application exited immediately - see %APPDATA%\Keyward\crash.log' -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host '  Could not launch the installed copy.' -ForegroundColor Red
        exit 1
    }
}

Write-Host ''
Write-Host '  Installed. Launch it from the Start Menu next time.' -ForegroundColor Green
Write-Host '  To uninstall: delete the install folder and the Start Menu shortcut.' -ForegroundColor DarkGray
Write-Host ''
