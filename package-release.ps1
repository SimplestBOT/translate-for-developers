# =============================================================
# package-release.ps1 - portable zip packer (repo-root, CI & local)
# Layout mirrors the v1.5 release; v1.7 additions: translator-uia.exe
# (UIA helper, required since P1 process isolation) + input.html page.
# Whitelist copy ONLY - never pick up pdb/config/logs from bin dirs.
# Usage: ./package-release.ps1 -Version v1.7.0   (out: release-pkg/*.zip)
# =============================================================
param(
    [string]$Version = "0.0.0-local",
    [string]$RepoRoot = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'

$hostBin = Join-Path $RepoRoot 'src\csharp\TranslatorHost\bin\Release\net48\win-x64'
$uiaBin  = Join-Path $RepoRoot 'src\csharp\TranslatorUia\bin\Release\net48'
$dist    = Join-Path $RepoRoot 'src\webui\dist'
$outDir  = Join-Path $RepoRoot 'release-pkg'
$stage   = Join-Path $outDir 'translate-for-developers'
$zip     = Join-Path $outDir ("translate-for-developers-$Version-portable.zip")

# ---- required files (whitelist) ----
$hostFiles = @(
    'translator-ui.exe',
    'TranslatorCore.dll',
    'WebView2Loader.dll',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'Microsoft.Web.WebView2.Wpf.dll'
)
$missing = @()
foreach ($f in $hostFiles) { if (-not (Test-Path (Join-Path $hostBin $f))) { $missing += "host/$f" } }
if (-not (Test-Path (Join-Path $uiaBin 'translator-uia.exe'))) { $missing += 'uia/translator-uia.exe' }
foreach ($p in @('settings.html','result.html','capture.html','config.html','input.html')) {
    if (-not (Test-Path (Join-Path $dist $p))) { $missing += "dist/$p" }
}
foreach ($p in @('start-translator.bat', 'src\icon.ico', 'packaging')) {
    if (-not (Test-Path (Join-Path $RepoRoot $p))) { $missing += $p }
}
if ($missing.Count -gt 0) {
    throw ("missing build outputs (run builds first): " + ($missing -join ', '))
}

# ---- stage ----
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'scripts\bridge') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'webui\dist') | Out-Null

foreach ($f in $hostFiles) {
    Copy-Item (Join-Path $hostBin $f) (Join-Path $stage 'scripts\bridge\')
}
Copy-Item (Join-Path $uiaBin 'translator-uia.exe') (Join-Path $stage 'scripts\bridge\')
Copy-Item (Join-Path $dist '*.html') (Join-Path $stage 'webui\dist\')
Copy-Item (Join-Path $RepoRoot 'src\icon.ico') (Join-Path $stage 'icon.ico')
Copy-Item (Join-Path $RepoRoot 'start-translator.bat') $stage
Get-ChildItem (Join-Path $RepoRoot 'packaging') -Filter '*.txt' | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $stage $_.Name)
}

# ---- self-check: no secrets/logs/pdb/legacy exe must ever ship ----
$bad = Get-ChildItem $stage -Recurse -File |
    Where-Object { $_.Name -match '\.(pdb|log|conf)$' -or $_.Name -eq 'translator.exe' }
if ($bad) { throw ("forbidden file in package: " + (($bad | ForEach-Object FullName) -join ', ')) }

# ---- zip + hash ----
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $zip).Length
Write-Output ("ZIP=" + $zip)
Write-Output ("SIZE=" + $size)
Write-Output ("SHA256=" + $hash)
Write-Output "PACKAGE-DONE"
