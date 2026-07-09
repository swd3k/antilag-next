#Requires -Version 5.1
<#
.SYNOPSIS
  Publish AntiLag Next to dist\AntiLagNext (framework-dependent win-x64).
.PARAMETER SelfContained
  If set, bundles the .NET runtime (larger zip).
#>
param(
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$slnRoot = Join-Path $root "AntiLagNext"
$out = Join-Path $root "dist\AntiLagNext"

$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

Push-Location $slnRoot
try {
  if (Test-Path $out) { Remove-Item $out -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $out | Out-Null

  $sc = if ($SelfContained) { "true" } else { "false" }
  Write-Host "Publishing SelfContained=$sc → $out"
  dotnet publish src\AntiLagNext.App\AntiLagNext.App.csproj `
    -c Release -r win-x64 `
    --self-contained $sc `
    -p:PublishSingleFile=false `
    -o $out

  if ($LASTEXITCODE -ne 0) { throw "publish failed" }

  # Ship root logo next to exe for readme / branding convenience
  Copy-Item (Join-Path $root "logo.png") (Join-Path $out "logo.png") -Force
  Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE") -Force

  # Zip
  $zip = Join-Path $root "dist\AntiLagNext-win-x64.zip"
  if (Test-Path $zip) { Remove-Item $zip -Force }
  Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -Force
  Write-Host "OK: $out"
  Write-Host "ZIP: $zip ($([math]::Round((Get-Item $zip).Length/1MB, 2)) MB)"
}
finally {
  Pop-Location
}
