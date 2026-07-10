#Requires -Version 5.1
<#
.SYNOPSIS
  Publish self-contained UI packages and build Setup.exe (includes .NET runtime).

.DESCRIPTION
  Framework-dependent installers are small (~2–3 MB) but need Desktop Runtime.
  Self-contained installers are larger (~60–80 MB) but run without installing .NET.

.PARAMETER Version
  Version for the installer (default 1.0.1).

.PARAMETER Rid
  Target RIDs (default win-x64 only — most users).
#>
param(
  [string]$Version = "1.0.1",
  [string[]]$Rid = @("win-x64")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "=== Self-contained publish + Setup.exe ===" -ForegroundColor Cyan
Write-Host "RIDs: $($Rid -join ', ')  Version: $Version"

& (Join-Path $PSScriptRoot "build-installer.ps1") `
  -Version $Version `
  -Rid $Rid `
  -PublishFirst `
  -SelfContained

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Rename outputs so SC builds are not confused with FDD
$inst = Join-Path $root "dist\installers"
foreach ($r in $Rid) {
  $src = Join-Path $inst "AntiLagNext-Setup-$r.exe"
  $dst = Join-Path $inst "AntiLagNext-Setup-$r-SC.exe"
  if (Test-Path $src) {
    if (Test-Path $dst) { Remove-Item $dst -Force }
    Move-Item $src $dst -Force
    $mb = [math]::Round((Get-Item $dst).Length / 1MB, 2)
    Write-Host "SC installer: $dst ($mb MB)" -ForegroundColor Green
  }
}

Write-Host "Done. Self-contained Setup packages are under dist\installers\*-SC.exe" -ForegroundColor Green
