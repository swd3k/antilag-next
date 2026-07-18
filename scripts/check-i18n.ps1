# Verify Photino i18n: every data-i18n / data-tip key exists in en.json and ru.json
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$html = Join-Path $root "AntiLagNext\src\AntiLagNext.Ui\wwwroot\index.html"
$enPath = Join-Path $root "AntiLagNext\src\AntiLagNext.Ui\wwwroot\i18n\en.json"
$ruPath = Join-Path $root "AntiLagNext\src\AntiLagNext.Ui\wwwroot\i18n\ru.json"

if (-not (Test-Path $html)) { throw "Missing $html" }

$htmlText = Get-Content $html -Raw -Encoding UTF8
$keys = [regex]::Matches($htmlText, 'data-(?:i18n|tip)="([^"]+)"') |
  ForEach-Object { $_.Groups[1].Value } |
  Sort-Object -Unique

function Get-JsonKeys([string]$path) {
  $obj = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
  return @($obj.PSObject.Properties.Name)
}

$en = Get-JsonKeys $enPath
$ru = Get-JsonKeys $ruPath
$enSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$en, [StringComparer]::Ordinal)
$ruSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$ru, [StringComparer]::Ordinal)

$missingEn = @()
$missingRu = @()
foreach ($k in $keys) {
  if (-not $enSet.Contains($k)) { $missingEn += $k }
  if (-not $ruSet.Contains($k)) { $missingRu += $k }
}

# Keys present in one pack but not the other
$onlyEn = $en | Where-Object { -not $ruSet.Contains($_) }
$onlyRu = $ru | Where-Object { -not $enSet.Contains($_) }

Write-Host "HTML data-i18n/tip keys: $($keys.Count)"
Write-Host "en.json keys: $($en.Count)  ru.json keys: $($ru.Count)"

$fail = $false
if ($missingEn.Count) {
  $fail = $true
  Write-Host "MISSING in en.json ($($missingEn.Count)):" -ForegroundColor Red
  $missingEn | ForEach-Object { Write-Host "  $_" }
}
if ($missingRu.Count) {
  $fail = $true
  Write-Host "MISSING in ru.json ($($missingRu.Count)):" -ForegroundColor Red
  $missingRu | ForEach-Object { Write-Host "  $_" }
}
if ($onlyEn.Count) {
  Write-Host "Only in en.json (warn, $($onlyEn.Count)):" -ForegroundColor Yellow
  $onlyEn | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
}
if ($onlyRu.Count) {
  Write-Host "Only in ru.json (warn, $($onlyRu.Count)):" -ForegroundColor Yellow
  $onlyRu | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
}

if ($fail) {
  Write-Host "i18n check FAILED" -ForegroundColor Red
  exit 1
}
Write-Host "i18n check OK" -ForegroundColor Green
exit 0
