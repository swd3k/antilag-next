# Improvements backlog & shipped notes

## Shipped (this pass)

| Item | Status |
|------|--------|
| **Backup** before changes | Local zip + git tag `backup/pre-improvements-*` |
| **Settings migration** schema v2 | Legacy RU preset names → English; ensure MaxPerformance |
| **Active profile i18n** | Photino + WPF + logs use culture-aware labels |
| **Engine log localization** | Known ProfileService/Settings messages mapped via `L()` |
| **a11y** | `aria-live` / `aria-label` on Active profile card |
| **Photino i18n smoke tests** | `en.json` must not contain Cyrillic profile labels |
| **Self-contained Setup script** | `scripts/build-setup-selfcontained.ps1` |

## Recommended next

1. **Code signing** — EV/OV Authenticode certificate to reduce SmartScreen on Setup.exe (paid; not automatable without a cert).
2. **Single multi-arch Setup** — one Inno script that ships x64 + arm64 + x86 payloads (`64BitThreeArch` pattern).
3. **Error codes API** — replace free-form `OperationResult.Message` with stable codes + UI i18n for 100% log localization.
4. **Telemetry-free crash upload** — optional local “export diagnostics” zip only.
5. **UI snapshot tests** — Playwright against Photino is heavy; keep JSON pack tests + manual checklist.

## Build recipes

```powershell
# Portable multi-arch + FDD Setup.exe
.\scripts\publish-all.ps1
.\scripts\build-installer.ps1 -Version 1.0.1

# Self-contained Setup (large, no .NET install required)
.\scripts\build-setup-selfcontained.ps1 -Version 1.0.1 -Rid win-x64
```
