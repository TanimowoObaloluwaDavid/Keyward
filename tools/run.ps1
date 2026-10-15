param(
    [string]$Out = 'C:\Users\LENOVO\projects\keyward\artifacts\shot.png',
    [switch]$KeepVault,
    [switch]$NoBuild
)

# Restarts Keyward and captures its window. Used for visual QA during development.

$root = 'C:\Users\LENOVO\projects\keyward'
$art  = Join-Path $root 'artifacts'
$exe  = Join-Path $art 'Keyward.exe'

Get-Process -Name 'Keyward' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700

if (-not $NoBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') | Out-Null
    if (-not (Test-Path $exe)) { Write-Output 'build failed'; exit 1 }
}

$appData = Join-Path $env:APPDATA 'Keyward'
if (-not $KeepVault) {
    Remove-Item (Join-Path $appData 'vault.kvw') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $appData 'settings.json') -Force -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $appData 'crash.log') -Force -ErrorAction SilentlyContinue

$proc = $null
for ($i = 0; $i -lt 4; $i++) {
    try { $proc = Start-Process $exe -PassThru -ErrorAction Stop; break }
    catch { Start-Sleep -Milliseconds 900 }
}

if (-not $proc) { Write-Output 'could not start'; exit 1 }

Start-Sleep -Seconds 5
$alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
if (-not $alive) {
    Write-Output 'process exited during startup'
    if (Test-Path (Join-Path $appData 'crash.log')) {
        Get-Content (Join-Path $appData 'crash.log') | Select-Object -First 14
    }
    exit 1
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\shot.ps1') -Out $Out
