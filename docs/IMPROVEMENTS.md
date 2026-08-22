# Improvements backlog & shipped notes

## Shipped (1.4.1)

| Item | Status |
|------|--------|
| **SHA256-verified silent Setup** | Matching hash from the same release `SHA256SUMS.txt` required; download under `%ProgramData%\AntiLagNext\update` |
| **System32 native tools** | `powercfg` / `schtasks` / `ipconfig` / `shutdown` — no PATH |
| **Session-scoped crash restore** | `LoadBySessionId`; planted “latest” JSON is ignored |
| **Denied registry values** | `ImagePath`, `NameServer`, `AppInit_DLLs`, `Debugger`, … blocked on restore |
| **SemVer / PE** | Prerelease tags ignored by updater; PE probe fails closed |
| **Chip logo in the app** | Window / tray / Inno ICO (DIB 16–48 + PNG 64–256) |

## Shipped (1.4.0)

| Item | Status |
|------|--------|
| **Win11 global timer** | `GlobalTimerResolutionRequests` catalog + `timeBeginPeriod` + UI scope |
| **AC-only power / no DC poison** | min CPU / ASPM never written to DC; skip scheme switch on battery |
| **HAGS + RAM-trim off by default** | schema v3 migrates built-in Gaming/Max once |
| **CI** | Infrastructure.Tests + check-i18n on ci.yml / release.yml / hard-test |
| **SHA256SUMS.txt** | attached to GitHub Releases |
| **Dead Native.dll P/Invoke** | removed |

## Shipped (earlier)

| Item | Status |
|------|--------|
| **Backup** before changes | Local zip + git tag `backup/pre-improvements-*` |
| **Settings migration** schema v2 | Legacy RU preset names → English; ensure MaxPerformance |
| **Active profile i18n** | Photino + logs use culture-aware labels |
| **Engine log localization** | Known ProfileService/Settings messages mapped via `L()` |
| **a11y** | `aria-live` / `aria-label` on Active profile card |
| **Photino i18n smoke tests** | `en.json` must not contain Cyrillic profile labels |
| **Self-contained Setup script** | `scripts/build-setup-selfcontained.ps1` |
| **Diagnostics export** | Local zip (1.3.0) |

## Recommended next

1. **Code signing** — EV/OV Authenticode certificate to reduce SmartScreen on Setup.exe (paid; not automatable without a cert).
2. **Per-game apply/revert** — `GameDetectionService` is registered in DI but unused; wire auto apply when a listed exe starts.
3. **Split Photino host** — `Program.cs` / `index.html` god files.
4. **Error codes API** — replace free-form `OperationResult.Message` with stable codes + UI i18n for 100% log localization.
5. **Sample external plugin** project + hide experimental stubs behind a developer toggle.
6. **Single multi-arch Setup** — one Inno script that ships x64 + arm64 + x86 payloads (`64BitThreeArch` pattern).

## Build recipes

```powershell
# Portable multi-arch + FDD Setup.exe
.\scripts\publish-all.ps1
.\scripts\build-installer.ps1 -Version 1.4.1

# Self-contained Setup (large, no .NET install required)
.\scripts\build-setup-selfcontained.ps1 -Version 1.4.1 -Rid win-x64
```
