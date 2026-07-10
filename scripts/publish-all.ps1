#Requires -Version 5.1
<#
.SYNOPSIS
  Publish AntiLag Next (Photino UI + CLI) for all Windows CPU architectures:
    - win-x64   (Intel/AMD 64-bit)
    - win-x86   (32-bit)
    - win-arm64 (Snapdragon / ARM64 Windows)

.PARAMETER SelfContained
  Bundle .NET 8 runtime (much larger). Default is framework-dependent.

.PARAMETER SkipZip
  Do not create .zip archives.

.PARAMETER AllowOversize
  Skip UI size gate (only applied for framework-dependent default builds).
#>
param(
  [switch]$SelfContained,
  [switch]$SkipZip,
  [switch]$AllowOversize,
  [double]$MaxSizeMb = 5
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$slnRoot = Join-Path $root "AntiLagNext"
$distRoot = Join-Path $root "dist"
$uiProj = "src\AntiLagNext.Ui\AntiLagNext.Ui.csproj"
$cliProj = "src\AntiLagNext.Cli\AntiLagNext.Cli.csproj"

$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

# Official .NET Windows RIDs for desktop
$targets = @(
  @{ Rid = "win-x64";   Label = "64-bit (x64 / Intel & AMD)" },
  @{ Rid = "win-x86";   Label = "32-bit (x86)" },
  @{ Rid = "win-arm64"; Label = "ARM64 (Windows on ARM)" }
)

$scFlag = if ($SelfContained) { "true" } else { "false" }

Write-Host "=== AntiLag Next multi-arch publish ===" -ForegroundColor Cyan
Write-Host "SelfContained=$scFlag"
Write-Host "Output root: $distRoot"
Write-Host ""

if (-not (Test-Path $distRoot)) {
  New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
}

function Get-DirSizeMb([string]$path) {
  $sum = (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
  if (-not $sum) { return 0 }
  return [math]::Round($sum / 1MB, 2)
}

function Get-PeMachine([string]$exePath) {
  try {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    switch ($machine) {
      0x014c { return "i386 (x86)" }
      0x8664 { return "AMD64 (x64)" }
      0xAA64 { return "ARM64" }
      default { return ("0x{0:X4}" -f $machine) }
    }
  } catch {
    return "unknown"
  }
}

function Publish-One([string]$project, [string]$rid, [string]$outDir) {
  if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $outDir | Out-Null

  Push-Location $slnRoot
  try {
    & dotnet publish $project `
      -c Release `
      -r $rid `
      --self-contained $scFlag `
      -p:PublishSingleFile=$false `
      -p:PublishTrimmed=$false `
      -p:DebugType=None `
      -p:DebugSymbols=false `
      -o $outDir
    if ($LASTEXITCODE -ne 0) {
      throw "dotnet publish failed: $project RID=$rid"
    }
  } finally {
    Pop-Location
  }
}

$results = @()
$failed = @()

foreach ($t in $targets) {
  $rid = $t.Rid
  $uiOut = Join-Path $distRoot "AntiLagNext-$rid"
  $cliOut = Join-Path $distRoot "AntiLagNext-cli-$rid"

  Write-Host ""
  Write-Host "=== $($t.Label)  RID=$rid ===" -ForegroundColor Green

  try {
    Write-Host "  UI  → $uiOut"
    Publish-One $uiProj $rid $uiOut

    # Branding
    $ico = Join-Path $slnRoot "src\AntiLagNext.Ui\Assets\app.ico"
    if (Test-Path $ico) {
      Copy-Item $ico (Join-Path $uiOut "logo.ico") -Force -ErrorAction SilentlyContinue
    }
    $lic = Join-Path $root "LICENSE"
    if (Test-Path $lic) { Copy-Item $lic (Join-Path $uiOut "LICENSE") -Force }

    # Ensure wwwroot present
    $html = Join-Path $uiOut "wwwroot\index.html"
    if (-not (Test-Path $html)) {
      Copy-Item (Join-Path $slnRoot "src\AntiLagNext.Ui\wwwroot\*") (Join-Path $uiOut "wwwroot") -Recurse -Force
    }

    $uiExe = Join-Path $uiOut "AntiLagNext.exe"
    if (-not (Test-Path $uiExe)) { throw "AntiLagNext.exe missing in $uiOut" }
    $uiPe = Get-PeMachine $uiExe
    $uiMb = Get-DirSizeMb $uiOut
    Write-Host "  UI  PE=$uiPe  size=$uiMb MB" -ForegroundColor Yellow

    if (-not $SelfContained -and -not $AllowOversize -and $uiMb -gt $MaxSizeMb) {
      throw "UI $rid size $uiMb MB exceeds MaxSizeMb=$MaxSizeMb"
    }

    Write-Host "  CLI → $cliOut"
    Publish-One $cliProj $rid $cliOut
    if (Test-Path $lic) { Copy-Item $lic (Join-Path $cliOut "LICENSE") -Force }
    $cliExe = Join-Path $cliOut "AntiLagNext.Cli.exe"
    if (-not (Test-Path $cliExe)) { throw "AntiLagNext.Cli.exe missing in $cliOut" }
    $cliPe = Get-PeMachine $cliExe
    $cliMb = Get-DirSizeMb $cliOut
    Write-Host "  CLI PE=$cliPe  size=$cliMb MB" -ForegroundColor Yellow

    # Also publish "main" aliases for x64 (compat with docs/hard-test)
    if ($rid -eq "win-x64") {
      $mainUi = Join-Path $distRoot "AntiLagNext"
      $mainCli = Join-Path $distRoot "AntiLagNext-cli"
      if (Test-Path $mainUi) { Remove-Item $mainUi -Recurse -Force }
      if (Test-Path $mainCli) { Remove-Item $mainCli -Recurse -Force }
      Copy-Item $uiOut $mainUi -Recurse -Force
      Copy-Item $cliOut $mainCli -Recurse -Force
      Write-Host "  Aliased → dist\AntiLagNext + dist\AntiLagNext-cli" -ForegroundColor Cyan
    }

    if (-not $SkipZip) {
      $zipUi = Join-Path $distRoot "AntiLagNext-$rid.zip"
      $zipCli = Join-Path $distRoot "AntiLagNext-cli-$rid.zip"
      if (Test-Path $zipUi) { Remove-Item $zipUi -Force }
      if (Test-Path $zipCli) { Remove-Item $zipCli -Force }
      Compress-Archive -Path (Join-Path $uiOut "*") -DestinationPath $zipUi -Force
      Compress-Archive -Path (Join-Path $cliOut "*") -DestinationPath $zipCli -Force
      Write-Host "  ZIP UI  $zipUi  ($([math]::Round((Get-Item $zipUi).Length/1MB,2)) MB)"
      Write-Host "  ZIP CLI $zipCli ($([math]::Round((Get-Item $zipCli).Length/1MB,2)) MB)"
    }

    $results += [pscustomobject]@{
      Rid = $rid
      Label = $t.Label
      UiPe = $uiPe
      CliPe = $cliPe
      UiMb = $uiMb
      CliMb = $cliMb
      Status = "OK"
    }
  }
  catch {
    Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
    $failed += $rid
    $results += [pscustomobject]@{
      Rid = $rid
      Label = $t.Label
      UiPe = "-"
      CliPe = "-"
      UiMb = 0
      CliMb = 0
      Status = "FAIL: $($_.Exception.Message)"
    }
  }
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host

if ($failed.Count -gt 0) {
  Write-Host "Failed RIDs: $($failed -join ', ')" -ForegroundColor Red
  exit 1
}

Write-Host "All architectures published." -ForegroundColor Green
Write-Host "Folders under: $distRoot"
Get-ChildItem $distRoot -Directory | Select-Object Name | Format-Table -AutoSize | Out-String | Write-Host
if (-not $SkipZip) {
  Get-ChildItem $distRoot -File -Filter "*.zip" | Format-Table Name, @{N='MB';E={[math]::Round($_.Length/1MB,2)}} -AutoSize | Out-String | Write-Host
}
