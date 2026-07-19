#Requires -Version 5.1
<#
.SYNOPSIS
  Build Inno Setup installers (Setup.exe) for AntiLag Next.

.DESCRIPTION
  Compiles installer/AntiLagNext.iss for one or more Windows RIDs.
  Expects published UI folders under dist\AntiLagNext-<rid> (see publish-all.ps1).

.PARAMETER Rid
  One RID or list: win-x64, win-x86, win-arm64. Default: all three.

.PARAMETER Version
  App version embedded in the installer (e.g. 1.3.1). Default: 1.3.1

.PARAMETER PublishFirst
  Run multi-arch publish before building installers.

.PARAMETER SelfContained
  When used with -PublishFirst, publish self-contained packages.

.PARAMETER IsccPath
  Optional path to ISCC.exe. Auto-detected if omitted.
#>
param(
  [string[]]$Rid = @("win-x64", "win-x86", "win-arm64"),
  [string]$Version = "1.3.1",
  [switch]$PublishFirst,
  [switch]$SelfContained,
  [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"
# ISCC returns non-zero for help / some warnings — do not auto-throw on native codes
if (Get-Variable PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
  $PSNativeCommandUseErrorActionPreference = $false
}
$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root "installer\AntiLagNext.iss"
$outDir = Join-Path $root "dist\installers"

if (-not (Test-Path $iss)) {
  throw "Missing Inno script: $iss"
}

function Resolve-Iscc {
  param([string]$Hint)
  if ($Hint -and (Test-Path $Hint)) { return (Resolve-Path $Hint).Path }

  $candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
  )
  foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) { return $c }
  }

  $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }

  throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isinfo.php"
}

function Get-ArchFromRid([string]$rid) {
  switch ($rid.ToLowerInvariant()) {
    "win-x64"   { return "x64" }
    "win-x86"   { return "x86" }
    "win-arm64" { return "arm64" }
    default     { throw "Unsupported RID: $rid (use win-x64, win-x86, win-arm64)" }
  }
}

# Normalize version: strip leading v, ensure at least major.minor.patch
$ver = $Version.Trim()
if ($ver.StartsWith("v") -or $ver.StartsWith("V")) { $ver = $ver.Substring(1) }
if ($ver -notmatch '^\d+\.\d+\.\d+') {
  throw "Version must look like 1.0.0 (got: $Version)"
}

if ($PublishFirst) {
  $pub = Join-Path $PSScriptRoot "publish-all.ps1"
  Write-Host "=== Publish packages first ===" -ForegroundColor Cyan
  if ($SelfContained) {
    & $pub -SelfContained -AllowOversize
  } else {
    & $pub
  }
  if ($LASTEXITCODE -ne 0) { throw "publish-all.ps1 failed" }
}

$iscc = Resolve-Iscc -Hint $IsccPath
Write-Host "ISCC: $iscc" -ForegroundColor Cyan
Write-Host "Version: $ver"
Write-Host "RIDs: $($Rid -join ', ')"
Write-Host ""

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$built = @()
$failed = @()

foreach ($r in $Rid) {
  $arch = Get-ArchFromRid $r
  $sourceDir = Join-Path $root "dist\AntiLagNext-$r"
  $outName = "AntiLagNext-Setup-$ver-$r"
  $exePath = Join-Path $outDir "$outName.exe"

  Write-Host "=== Build Setup $r ($arch) ===" -ForegroundColor Green

  if (-not (Test-Path $sourceDir)) {
    Write-Host "  SKIP: missing publish folder $sourceDir" -ForegroundColor Yellow
    Write-Host "  Run .\scripts\publish-all.ps1 or pass -PublishFirst" -ForegroundColor Yellow
    $failed += $r
    continue
  }

  $uiExe = Join-Path $sourceDir "AntiLagNext.exe"
  if (-not (Test-Path $uiExe)) {
    Write-Host "  FAIL: AntiLagNext.exe not found in $sourceDir" -ForegroundColor Red
    $failed += $r
    continue
  }

  # Inno wants absolute or relative-to-iss paths; pass absolute SourceDir
  $sourceAbs = (Resolve-Path $sourceDir).Path

  $args = @(
    $iss,
    "/DArch=$arch",
    "/DSourceDir=$sourceAbs",
    "/DOutName=$outName",
    "/DMyAppVersion=$ver",
    "/Q"
  )

  & $iscc @args
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: ISCC exit $LASTEXITCODE" -ForegroundColor Red
    $failed += $r
    continue
  }

  if (-not (Test-Path $exePath)) {
    Write-Host "  FAIL: expected output missing: $exePath" -ForegroundColor Red
    $failed += $r
    continue
  }

  $mb = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
  Write-Host "  OK: $exePath ($mb MB)" -ForegroundColor Yellow
  $built += [pscustomobject]@{ Rid = $r; Path = $exePath; Mb = $mb }
}

Write-Host ""
Write-Host "=== INSTALLER SUMMARY ===" -ForegroundColor Cyan
if ($built.Count -gt 0) {
  $built | Format-Table -AutoSize | Out-String | Write-Host
} else {
  Write-Host "(none built)" -ForegroundColor Yellow
}

if ($failed.Count -gt 0) {
  Write-Host "Failed/skipped: $($failed -join ', ')" -ForegroundColor Red
  exit 1
}

Write-Host "All Setup.exe packages ready under: $outDir" -ForegroundColor Green
Get-ChildItem $outDir -Filter "AntiLagNext-Setup-*.exe" |
  Format-Table Name, @{N = "MB"; E = { [math]::Round($_.Length / 1MB, 2) } } -AutoSize |
  Out-String | Write-Host
