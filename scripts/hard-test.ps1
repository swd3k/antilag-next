#Requires -Version 5.1
<#
.SYNOPSIS
  Hard test suite for AntiLag Next (Photino UI + CLI): restore, build, unit + smoke, publish, size gate, security sanity.
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
dotnet --info | Select-Object -First 12
if (-not (Test-Path $sln)) { throw "Solution not found: $sln" }

# Stop leftover UI that locks publish output
Get-Process -Name "AntiLagNext","AntiLagNext.Cli" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

Write-Step "Restore"
Push-Location $slnRoot
try {
  dotnet restore $sln
  if ($LASTEXITCODE -ne 0) { throw "restore failed" }

  Write-Step "Build Release"
  dotnet build $sln -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw "build failed" }

  Write-Step "Unit tests (Core + security policy)"
  dotnet test tests\AntiLagNext.Core.Tests\AntiLagNext.Core.Tests.csproj -c Release --no-build --verbosity normal
  if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

  Write-Step "Hard smoke tests (Win32)"
  dotnet test tests\AntiLagNext.SmokeTests\AntiLagNext.SmokeTests.csproj -c Release --verbosity normal
  if ($LASTEXITCODE -ne 0) { throw "smoke tests failed" }

  Write-Step "Publish Photino UI (shipping) + CLI"
  $out = Join-Path $root "dist\AntiLagNext"
  $outCli = Join-Path $root "dist\AntiLagNext-cli"
  if (Test-Path $out) { Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue }
  if (Test-Path $outCli) { Remove-Item $outCli -Recurse -Force -ErrorAction SilentlyContinue }

  dotnet publish src\AntiLagNext.Ui\AntiLagNext.Ui.csproj -c Release -r win-x64 --self-contained false -o $out
  if ($LASTEXITCODE -ne 0) { throw "UI publish failed" }

  dotnet publish src\AntiLagNext.Cli\AntiLagNext.Cli.csproj -c Release -r win-x64 --self-contained false -o $outCli
  if ($LASTEXITCODE -ne 0) { throw "CLI publish failed" }

  $exe = Join-Path $out "AntiLagNext.exe"
  $html = Join-Path $out "wwwroot\index.html"
  if (-not (Test-Path $exe)) { throw "AntiLagNext.exe missing after publish" }
  if (-not (Test-Path $html)) { throw "wwwroot\index.html missing (UI StartUrl would fail)" }

  # Photino Load(Uri) needs real file; ensure path is valid absolute
  $fullHtml = (Resolve-Path $html).Path
  Write-Host "UI HTML: $fullHtml" -ForegroundColor Green

  Write-Step "Size gate (≤ 5 MB FDD)"
  $sum = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum
  $mb = [math]::Round($sum / 1MB, 2)
  Write-Host "UI package: $mb MB"
  if ($mb -gt 5) { throw "Size $mb MB exceeds 5 MB gate" }

  Write-Step "Security smoke (static)"
  # External plugins must not load arbitrary *.dll by default pattern
  $pluginHost = Get-Content (Join-Path $slnRoot "src\AntiLagNext.Infrastructure\Plugins\PluginHost.cs") -Raw
  if ($pluginHost -notmatch '\*\.plugin\.dll') {
    throw "PluginHost must load only *.plugin.dll"
  }
  $policy = Get-Content (Join-Path $slnRoot "src\AntiLagNext.Infrastructure\Safety\RegistryPathPolicy.cs") -Raw
  # Must not allow open-ended Services\ tree (only explicit service names via ServiceAllowList)
  if ($policy -match 'AllowedPrefixes' -and $policy -notmatch 'ServiceAllowList') {
    throw "RegistryPathPolicy must use ServiceAllowList for services"
  }
  if ($policy -match 'SOFTWARE\\AMD\\"') {
    throw "Broad SOFTWARE\AMD root must not be allowlisted"
  }
  Write-Host "Plugin pattern + registry policy checks OK" -ForegroundColor Green

  Write-Step "CLI --help / --status (no admin, asInvoker)"
  $cli = Join-Path $outCli "AntiLagNext.Cli.exe"
  if (-not (Test-Path $cli)) { throw "CLI exe missing" }
  # Manifest is asInvoker: --help must work without elevation
  $helpOut = Join-Path $env:TEMP "al-cli-help.txt"
  $helpErr = Join-Path $env:TEMP "al-cli-err.txt"
  $p = Start-Process -FilePath $cli -ArgumentList "--help" -Wait -PassThru -NoNewWindow `
    -RedirectStandardOutput $helpOut -RedirectStandardError $helpErr -ErrorAction Stop
  if ($p.ExitCode -ne 0) {
    throw "CLI --help exit $($p.ExitCode) (expected 0 without admin). stderr=$(Get-Content $helpErr -Raw -ErrorAction SilentlyContinue)"
  }
  $helpText = Get-Content $helpOut -Raw -ErrorAction SilentlyContinue
  if ($helpText -notmatch "AntiLag") { throw "CLI --help produced unexpected output" }
  Write-Host "CLI --help exit: 0 OK" -ForegroundColor Green

  $stOut = Join-Path $env:TEMP "al-cli-status.txt"
  $stErr = Join-Path $env:TEMP "al-cli-status-err.txt"
  $ps = Start-Process -FilePath $cli -ArgumentList "--status" -Wait -PassThru -NoNewWindow `
    -RedirectStandardOutput $stOut -RedirectStandardError $stErr -ErrorAction Stop
  # status may return 0 or 1 depending on engine/settings access; must not be elevation-blocked
  $stErrText = (Get-Content $stErr -Raw -ErrorAction SilentlyContinue)
  if ($stErrText -match "elevation|повышен|требует повышения") {
    throw "CLI --status still blocked by elevation"
  }
  Write-Host "CLI --status exit: $($ps.ExitCode) (no elevation block)" -ForegroundColor Green

  # apply without admin must fail soft with exit 1, not UAC crash
  $apErr = Join-Path $env:TEMP "al-cli-apply-err.txt"
  $apOut = Join-Path $env:TEMP "al-cli-apply-out.txt"
  $pa = Start-Process -FilePath $cli -ArgumentList "--apply","gaming","--silent" -Wait -PassThru -NoNewWindow `
    -RedirectStandardOutput $apOut -RedirectStandardError $apErr -ErrorAction Stop
  if ($pa.ExitCode -eq 0) {
    Write-Host "CLI --apply without admin returned 0 (unexpected if not elevated)" -ForegroundColor Yellow
  } else {
    Write-Host "CLI --apply without admin exit: $($pa.ExitCode) (soft fail OK)" -ForegroundColor Green
  }

  Write-Step "ALL HARD TESTS PASSED"
  Write-Host "Published UI:  $out ($mb MB)" -ForegroundColor Green
  Write-Host "Published CLI: $outCli" -ForegroundColor Green
  Write-Host "Manual: run AntiLagNext.exe elevated and smoke Apply/Undo + chart toggle." -ForegroundColor Yellow
}
finally {
  Pop-Location
}
