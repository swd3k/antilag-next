#Requires -Version 5.1
<#
.SYNOPSIS
  Собирает AntiLag Next в exe-пакеты:
    - 32-bit  (win-x86)
    - 64-bit  (win-x64)
    - AMD64   (win-x64, тот же код, отдельная папка/zip для AMD-систем)

  Self-contained: runtime .NET 8 внутри, на машине SDK не нужен.
#>
param(
  [switch]$FrameworkDependent  # если нужен меньший размер (нужен .NET 8 Desktop Runtime)
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$slnRoot = Join-Path $root "AntiLagNext"
$distRoot = Join-Path $root "dist"
$proj = "src\AntiLagNext.App\AntiLagNext.App.csproj"

$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

# Имя папки, RID, описание
$targets = @(
  @{ Name = "x86-32bit";   Rid = "win-x86";   Label = "32-bit (x86)" },
  @{ Name = "x64-64bit";   Rid = "win-x64";   Label = "64-bit (x64)" },
  @{ Name = "amd64";       Rid = "win-x64";   Label = "AMD64 (x64, AMD/Intel 64)" }
)

$sc = -not $FrameworkDependent
$scFlag = if ($sc) { "true" } else { "false" }

Write-Host "SelfContained=$scFlag" -ForegroundColor Cyan
Write-Host "Output: $distRoot" -ForegroundColor Cyan

if (-not (Test-Path $distRoot)) {
  New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
}

Push-Location $slnRoot
try {
  # Кэш уже собранного RID, чтобы amd64 не билдить дважды
  $builtRid = @{}

  foreach ($t in $targets) {
    $out = Join-Path $distRoot "AntiLagNext-$($t.Name)"
    Write-Host ""
    Write-Host "=== $($t.Label)  RID=$($t.Rid)  →  $out ===" -ForegroundColor Green

    if ($builtRid.ContainsKey($t.Rid) -and (Test-Path $builtRid[$t.Rid])) {
      Write-Host "Reuse build from $($builtRid[$t.Rid])"
      if (Test-Path $out) { Remove-Item $out -Recurse -Force }
      Copy-Item -Path $builtRid[$t.Rid] -Destination $out -Recurse -Force
    }
    else {
      if (Test-Path $out) { Remove-Item $out -Recurse -Force }
      New-Item -ItemType Directory -Force -Path $out | Out-Null

      dotnet publish $proj `
        -c Release `
        -r $t.Rid `
        --self-contained $scFlag `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $out

      if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for RID=$($t.Rid) ($($t.Label))"
      }

      $builtRid[$t.Rid] = $out
    }

    # Брендинг рядом с exe
    Copy-Item (Join-Path $root "logo.png") (Join-Path $out "logo.png") -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE") -Force -ErrorAction SilentlyContinue

    $exe = Join-Path $out "AntiLagNext.exe"
    if (-not (Test-Path $exe)) {
      throw "AntiLagNext.exe not found in $out"
    }

    # PE architecture check (optional)
    try {
      $bytes = [System.IO.File]::ReadAllBytes($exe)
      $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
      $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
      $arch = switch ($machine) {
        0x014c { "i386 (32-bit)" }
        0x8664 { "AMD64 (64-bit)" }
        0xAA64 { "ARM64" }
        default { "0x{0:X4}" -f $machine }
      }
      Write-Host "  PE machine: $arch" -ForegroundColor Yellow
    } catch {
      Write-Host "  (PE probe skipped)"
    }

    $sizeMb = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 2)
    Write-Host "  Size: $sizeMb MB"

    # ZIP
    $zip = Join-Path $distRoot "AntiLagNext-$($t.Name).zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -Force
    $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 2)
    Write-Host "  ZIP: $zip ($zipMb MB)" -ForegroundColor Cyan
  }

  Write-Host ""
  Write-Host "=== DONE ===" -ForegroundColor Green
  Get-ChildItem $distRoot -File -Filter "AntiLagNext-*.zip" | Format-Table Name, @{N='MB';E={[math]::Round($_.Length/1MB,2)}} -AutoSize
  Get-ChildItem $distRoot -Directory -Filter "AntiLagNext-*" | ForEach-Object {
    $exe = Join-Path $_.FullName "AntiLagNext.exe"
    Write-Host ("{0,-40} exe={1}" -f $_.Name, (Test-Path $exe))
  }
}
finally {
  Pop-Location
}
