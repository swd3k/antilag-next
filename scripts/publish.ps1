#Requires -Version 5.1
<#
.SYNOPSIS
  Publish AntiLag Next (Photino UI + CLI) with size gate (default max 5 MB for portable payload).
.PARAMETER SelfContained
  Bundle .NET runtime (usually exceeds 5 MB — use -AllowOversize).
.PARAMETER AllowOversize
  Do not fail when output exceeds MaxSizeMb.
.PARAMETER MaxSizeMb
  Size gate in megabytes (default 5).
#>
param(
  [switch]$SelfContained,
  [switch]$AllowOversize,
  [double]$MaxSizeMb = 5
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$slnRoot = Join-Path $root "AntiLagNext"
$out = Join-Path $root "dist\AntiLagNext"
$outCli = Join-Path $root "dist\AntiLagNext-cli"

$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

Push-Location $slnRoot
try {
  if (Test-Path $out) { Remove-Item $out -Recurse -Force }
  if (Test-Path $outCli) { Remove-Item $outCli -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $out | Out-Null
  New-Item -ItemType Directory -Force -Path $outCli | Out-Null

  $sc = if ($SelfContained) { "true" } else { "false" }
  Write-Host "Publishing UI SelfContained=$sc → $out"

  # Primary ship path: lightweight Photino UI (not WPF)
  dotnet publish src\AntiLagNext.Ui\AntiLagNext.Ui.csproj `
    -c Release -r win-x64 `
    --self-contained $sc `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $out
  if ($LASTEXITCODE -ne 0) { throw "UI publish failed" }

  Write-Host "Publishing CLI → $outCli"
  dotnet publish src\AntiLagNext.Cli\AntiLagNext.Cli.csproj `
    -c Release -r win-x64 `
    --self-contained $sc `
    -p:PublishSingleFile=false `
    -o $outCli
  if ($LASTEXITCODE -ne 0) { throw "CLI publish failed" }

  # Branding: use compact app.ico only (avoid shipping 270KB logo twice)
  $logoApp = Join-Path $slnRoot "src\AntiLagNext.Ui\Assets\app.ico"
  if (Test-Path $logoApp) {
    Copy-Item $logoApp (Join-Path $out "logo.ico") -Force -ErrorAction SilentlyContinue
    Copy-Item $logoApp (Join-Path $outCli "logo.ico") -Force -ErrorAction SilentlyContinue
  }
  $lic = Join-Path $root "LICENSE"
  if (Test-Path $lic) {
    Copy-Item $lic (Join-Path $out "LICENSE") -Force
    Copy-Item $lic (Join-Path $outCli "LICENSE") -Force
  }

  # Portable marker template (optional)
  "" | Set-Content (Join-Path $out "AntiLagNext.portable.example") -Encoding ascii

  function Get-DirSizeMb($path) {
    $sum = (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if (-not $sum) { return 0 }
    return [math]::Round($sum / 1MB, 2)
  }

  $uiMb = Get-DirSizeMb $out
  $cliMb = Get-DirSizeMb $outCli
  Write-Host "UI size:  $uiMb MB  ($out)"
  Write-Host "CLI size: $cliMb MB ($outCli)"

  # Breakdown top files
  Write-Host "Top UI files:"
  Get-ChildItem $out -Recurse -File |
    Sort-Object Length -Descending |
    Select-Object -First 12 |
    ForEach-Object { "{0,8:N2} MB  {1}" -f ($_.Length/1MB), $_.FullName.Substring($out.Length+1) }

  if ($uiMb -gt $MaxSizeMb -and -not $AllowOversize) {
    throw "UI publish size $uiMb MB exceeds MaxSizeMb=$MaxSizeMb. Use -AllowOversize or reduce deps."
  }

  $zip = Join-Path $root "dist\AntiLagNext-win-x64.zip"
  if (Test-Path $zip) { Remove-Item $zip -Force }
  Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -Force
  Write-Host "OK: $out"
  Write-Host "ZIP: $zip ($([math]::Round((Get-Item $zip).Length/1MB, 2)) MB)"
}
finally {
  Pop-Location
}
