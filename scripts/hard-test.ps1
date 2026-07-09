#Requires -Version 5.1
<#
.SYNOPSIS
  Hard test suite for AntiLag Next: restore, build, unit + smoke tests, publish dry-run.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$slnRoot = Join-Path $root "AntiLagNext"
$sln = Join-Path $slnRoot "AntiLagNext.sln"

function Write-Step($msg) {
  Write-Host ""
  Write-Host "=== $msg ===" -ForegroundColor Cyan
}

Write-Step "Environment"
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
dotnet --info | Select-Object -First 20
if (-not (Test-Path $sln)) { throw "Solution not found: $sln" }

Write-Step "Restore"
Push-Location $slnRoot
try {
  dotnet restore $sln
  if ($LASTEXITCODE -ne 0) { throw "restore failed" }

  Write-Step "Build Release"
  dotnet build $sln -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw "build failed" }

  Write-Step "Unit tests (Core)"
  dotnet test tests\AntiLagNext.Core.Tests\AntiLagNext.Core.Tests.csproj -c Release --no-build --verbosity normal
  if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

  Write-Step "Hard smoke tests (Win32)"
  # Rebuild smoke project to ensure latest
  dotnet test tests\AntiLagNext.SmokeTests\AntiLagNext.SmokeTests.csproj -c Release --verbosity normal
  if ($LASTEXITCODE -ne 0) { throw "smoke tests failed" }

  Write-Step "Publish framework-dependent"
  $out = Join-Path $root "dist\AntiLagNext"
  if (Test-Path $out) { Remove-Item $out -Recurse -Force }
  dotnet publish src\AntiLagNext.App\AntiLagNext.App.csproj -c Release -r win-x64 --self-contained false -o $out
  if ($LASTEXITCODE -ne 0) { throw "publish failed" }

  $exe = Join-Path $out "AntiLagNext.exe"
  if (-not (Test-Path $exe)) { throw "AntiLagNext.exe missing after publish" }
  Write-Host "Published: $exe" -ForegroundColor Green
  Get-ChildItem $out | Format-Table Name, Length -AutoSize

  Write-Step "Sanity: PE + logo assets"
  if (-not (Test-Path (Join-Path $root "logo.png"))) { throw "root logo.png missing" }
  Write-Host "logo.png OK" -ForegroundColor Green

  Write-Step "ALL HARD TESTS PASSED"
  Write-Host "Note: full UI Apply/Reset under admin is manual smoke on the target machine." -ForegroundColor Yellow
}
finally {
  Pop-Location
}
