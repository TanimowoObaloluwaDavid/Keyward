<#
    Keyward build script.

    Compiles with the C# compiler that ships in the Windows .NET Framework directory.
    There is no MSBuild, NuGet, npm or Rust toolchain on the target machine, so the project is
    deliberately dependency-free and is compiled directly by csc.

        .\build.ps1                 build everything into .\artifacts
        .\build.ps1 -RunTests       build, then run the test suite
        .\build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'

$Root    = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
$Artifacts = Join-Path $Root 'artifacts'
$Log     = Join-Path $Artifacts 'build.log'

$Framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$Wpf       = Join-Path $Framework 'WPF'
$Csc       = Join-Path $Framework 'csc.exe'

if (-not (Test-Path $Csc)) {
    throw "C# compiler not found at $Csc"
}

New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null
if (Test-Path $Log) { Remove-Item $Log -Force }

function Write-Step([string]$Text) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text
    Write-Host $line
    Add-Content -LiteralPath $Log -Value $line
}

function Write-Detail([string]$Text) {
    foreach ($l in ($Text -split "`r?`n")) {
        if ($l.Trim()) { Write-Host "    $l"; Add-Content -LiteralPath $Log -Value "    $l" }
    }
}

$Optimize = if ($Configuration -eq 'Release') { '/optimize+' } else { '/optimize-' }
$DebugType = if ($Configuration -eq 'Release') { '/debug:pdbonly' } else { '/debug:full' }

# ---------------------------------------------------------------------------------------------
# Project layout
# ---------------------------------------------------------------------------------------------

$CoreSources = @(Get-ChildItem -Path (Join-Path $Root 'src\Core') -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName })
$UiSources   = @(Get-ChildItem -Path (Join-Path $Root 'src\Ui')   -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName })
$TestSources = @(Get-ChildItem -Path (Join-Path $Root 'tests')    -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName })

$CoreDll = Join-Path $Artifacts 'Keyward.Core.dll'
$UiExe   = Join-Path $Artifacts 'Keyward.exe'
$TestExe = Join-Path $Artifacts 'Keyward.Tests.exe'

# ---------------------------------------------------------------------------------------------
# Reference sets
# ---------------------------------------------------------------------------------------------

$BaseRefs = @(
    (Join-Path $Framework 'mscorlib.dll'),
    (Join-Path $Framework 'System.dll'),
    (Join-Path $Framework 'System.Core.dll'),
    (Join-Path $Framework 'System.Xml.dll'),
    (Join-Path $Framework 'System.Data.dll'),
    (Join-Path $Framework 'System.Drawing.dll'),
    (Join-Path $Framework 'System.Configuration.dll')
) | Where-Object { Test-Path $_ }

$WpfRefs = @(
    (Join-Path $Wpf 'PresentationFramework.dll'),
    (Join-Path $Wpf 'PresentationCore.dll'),
    (Join-Path $Wpf 'WindowsBase.dll'),
    (Join-Path $Framework 'System.Xaml.dll'),
    (Join-Path $Wpf 'System.Printing.dll')
) | Where-Object { Test-Path $_ }

function Invoke-Compiler {
    param(
        [string]$Target,
        [string]$OutputPath,
        [string[]]$Sources,
        [string[]]$References,
        [string[]]$ExtraArgs,
        [switch]$IsLibrary
    )

    if ($Sources.Count -eq 0) { throw "No sources found for $Target" }

    # Remove previous output first: csc cannot overwrite a file that another process still
    # holds open, and a stale lock would otherwise surface as a confusing CS0016.
    foreach ($stale in @($OutputPath, [System.IO.Path]::ChangeExtension($OutputPath, '.pdb'))) {
        if (Test-Path -LiteralPath $stale) {
            try { Remove-Item -LiteralPath $stale -Force -ErrorAction Stop }
            catch { throw "Cannot replace '$stale' because it is locked. Close any running copy of the app and rebuild. ($($_.Exception.Message))" }
        }
    }

    # NOTE: locals are deliberately not named $Output or $args. PowerShell variable names are
    # case-insensitive, so a local $output would silently overwrite the $Output parameter.
    $cscArgs = @('/nologo', '/noconfig', '/nostdlib+', '/utf8output', '/warn:4', '/warnaserror-')
    $cscArgs += '/langversion:5'
    $cscArgs += $Optimize
    $cscArgs += '/platform:anycpu'
    $cscArgs += $DebugType
    if ($IsLibrary) { $cscArgs += '/target:library' } else { $cscArgs += '/target:winexe' }
    if ($ExtraArgs) { $cscArgs += $ExtraArgs }

    foreach ($r in $References) {
        if ([string]::IsNullOrWhiteSpace($r)) { throw "Empty reference path supplied to $Target" }
        $cscArgs += "/reference:$r"
    }
    $cscArgs += "/out:$OutputPath"
    $cscArgs += $Sources

    $compilerOutput = & $Csc @cscArgs 2>&1 | Out-String
    $code = $LASTEXITCODE

    if ($compilerOutput.Trim()) { Write-Detail $compilerOutput.Trim() }
    if ($code -ne 0) { throw "$Target failed to compile (exit $code)." }

    $size = (Get-Item -LiteralPath $OutputPath).Length
    Write-Step ("  {0,-18} OK  {1,9:N0} bytes  ({2} files)" -f $Target, $size, $Sources.Count)
}

Write-Step "Keyward build - $Configuration"
Write-Step ("  compiler: {0}" -f $Csc)

# ---------------------------------------------------------------------------------------------
# 1. Core library (no UI dependencies)
# ---------------------------------------------------------------------------------------------
Write-Step 'Compiling Keyward.Core ...'
Invoke-Compiler -Target 'Keyward.Core.dll' -Output $CoreDll -Sources $CoreSources -References $BaseRefs -IsLibrary

# ---------------------------------------------------------------------------------------------
# 2. UI executable
# ---------------------------------------------------------------------------------------------
if ($UiSources.Count -gt 0) {
    Write-Step 'Compiling Keyward.exe ...'

    # The manifest declares DPI awareness. Without it Windows bitmap-stretches the window on a
    # scaled display, which blurs text and makes pointer hit-testing inaccurate.
    $Manifest = Join-Path $Root 'src\app.manifest'
    $Icon     = Join-Path $Root 'assets\Keyward.ico'
    $ExeFlags = @()
    if (Test-Path $Manifest) { $ExeFlags += "/win32manifest:$Manifest" }
    if (Test-Path $Icon)     { $ExeFlags += "/win32icon:$Icon" }

    Invoke-Compiler -Target 'Keyward.exe' -OutputPath $UiExe -Sources $UiSources `
                    -References ($BaseRefs + $WpfRefs + @($CoreDll)) -ExtraArgs $ExeFlags
} else {
    Write-Step 'No UI sources yet - skipping Keyward.exe'
}

# ---------------------------------------------------------------------------------------------
# 3. Test harness
# ---------------------------------------------------------------------------------------------
if ($TestSources.Count -gt 0) {
    Write-Step 'Compiling Keyward.Tests.exe ...'
    Invoke-Compiler -Target 'Keyward.Tests.exe' -Output $TestExe -Sources $TestSources -References ($BaseRefs + @($CoreDll))
} else {
    Write-Step 'No test sources yet - skipping test harness'
}

Write-Step 'Build complete.'

# ---------------------------------------------------------------------------------------------
# 4. Tests
# ---------------------------------------------------------------------------------------------
if ($RunTests) {
    if (-not (Test-Path $TestExe)) { throw 'Test harness was not built.' }
    Write-Step 'Running test suite ...'
    $testOutput = & $TestExe 2>&1 | Out-String
    Write-Host $testOutput
    Add-Content -LiteralPath $Log -Value $testOutput
    if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
    Write-Step 'All tests passed.'
}
