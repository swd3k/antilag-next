#Requires -Version 5.1
param(
  [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$isccCandidates = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  Write-Host "Installing Inno Setup via winget..."
  winget install JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
  $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $iscc) { throw "ISCC.exe not found" }
}

Write-Host "ISCC: $iscc"

if (-not $SkipPublish) {
  $need = @(
    "AntiLagNext-x86-32bit",
    "AntiLagNext-x64-64bit",
    "AntiLagNext-amd64"
  )
  $missing = @()
  foreach ($n in $need) {
    $exe = Join-Path $root "dist\$n\AntiLagNext.exe"
    if (-not (Test-Path $exe)) { $missing += $n }
  }
  if ($missing.Count -gt 0) {
    Write-Host "Missing publish folders, running publish-all.ps1..."
    & (Join-Path $PSScriptRoot "publish-all.ps1")
  }
}

$outDir = Join-Path $root "dist\installers"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$iss = Join-Path $root "installer\AntiLagNext.iss"
if (-not (Test-Path $iss)) { throw "ISS not found: $iss" }

$builds = @(
  @{ Arch = "x86";   Source = "dist\AntiLagNext-x86-32bit"; Out = "AntiLagNext-Setup-x86-32bit" },
  @{ Arch = "x64";   Source = "dist\AntiLagNext-x64-64bit"; Out = "AntiLagNext-Setup-x64-64bit" },
  @{ Arch = "amd64"; Source = "dist\AntiLagNext-amd64";     Out = "AntiLagNext-Setup-amd64" }
)

foreach ($b in $builds) {
  $src = Join-Path $root $b.Source
  $exePath = Join-Path $src "AntiLagNext.exe"
  if (-not (Test-Path $exePath)) {
    throw "Source missing: $exePath"
  }

  Write-Host ""
  Write-Host "=== Building $($b.Out) ==="

  $srcAbs = (Resolve-Path $src).Path
  & $iscc $iss "/DArch=$($b.Arch)" "/DSourceDir=$srcAbs" "/DOutName=$($b.Out)"
  if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed for $($b.Out)"
  }
}

Write-Host ""
Write-Host "=== Installers ready ==="
Get-ChildItem $outDir -Filter "AntiLagNext-Setup-*.exe" |
  Format-Table Name, @{N = "MB"; E = { [math]::Round($_.Length / 1MB, 2) } }, FullName -AutoSize
Write-Host "Folder: $outDir"
