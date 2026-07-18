# Changelog

All notable changes to **AntiLag Next** are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/).  
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

---

## [1.3.0] — 2026-07-18

### Added
- **First-run wizard** (3 steps): what the app does, what it does not, how to start safely.
- **What changed** panel after Enable — plain-language list from apply summary (area, risk, reboot hint).
- **Before / After** latency comparison on the dashboard: true **median over a sample window** (≥12 points / ~3 s before enable; 1.5 s settle + ~3 s collect after).
- **Health → Fix recommended** — Safe catalog fixes + reapply drifted **desired-state only** in one action.
- **Audit findings grouped by area** (Timer / Power / GPU / Network / …); single “System” subgroup not duplicated under “System audit”.
- **Export diagnostics** zip (redacted settings, audit, drift, logs) — Logs + Settings; opens Explorer to the file; keeps last 15 archives.
- `ApplyChangeSummary` / builder; `AuditFinding.Area`; optional `OperationResult.Code`.
- IPC: `fixRecommended`, `exportDiagnostics`, `completeFirstRun`; apply payload includes `changeSummary`.

### Fixed
- Before/After no longer uses a single last sample as a fake “median”.
- Chart **Y scale** extended to fixed rungs **200 / 500 / 1000 / 2000 / 5000 / 10000 / 15000 µs** (was capped at 5000).
- Peak / wire display ceiling raised to **15 ms**; probe glitch clamp to **20 ms** so spikes above 10 ms remain visible.
- Bottom status no longer flashes “app not responding” during long Enable/Health ops (“Working…”).
- Chart stays live during apply/revert/health (`keepChart`) so BA sampling and UI stay responsive.
- **RU localization** polish: natural copy (no “Enable” / “scheduling latency” calques), Health/wizard/diagnostics/tags, HTML first-paint fallbacks.
- Health: Fix recommended success = audit **and** drift; empty desired-state skips unnecessary drift backup session.
- Drift reapply never mass-applies full catalog when desired-state is empty.
- Diagnostics export: Explorer open only for paths under the diagnostics directory; unsafe path characters rejected.
- Engine/user-facing operation messages stabilized in English (backup/Win32/GameMode defaults) for EN UI.

### Changed
- Product version **1.3.0**.
- Dashboard layout: latency monitor + three status cards first; BA / What changed / update banner below Enable.
- Onboarding localStorage key `al_onboard_v2` (wizard).
- Typed `FixOpResult` for audit/health fix path (no reflection on anonymous payloads).

### Security / trust
- Diagnostics export is local-only (no cloud); settings redacted; no full backup dump.
- Drift reapply limited to previously owned desired-state entries only.

---

## [1.2.2] — 2026-07-18

### Security
- **Registry restore allowlist** requires a path boundary after each prefix (blocks sibling-key bypass such as `SOFTWARE\AntiLagNextEvil`).
- **Update download** always uses the canonical Setup URL for version+RID (ignores remote `browser_download_url` / asset name).
- Download host allowlist tightened: only `github.com/…/releases/download/*.exe` and official GitHub CDN hosts — **not** `raw.githubusercontent.com` or arbitrary `*.githubusercontent.com`.
- Reject download redirects outside the allowlist; **120 MB** size cap; **MZ/PE** header check before launching Setup.
- Release / open-URL shell opens only allowlisted HTTPS GitHub pages for this repo (no `file:` / foreign hosts).
- **`restartPc` IPC** requires `confirmed: true`; `shutdown.exe` resolved under System32 (PATH hijack mitigation).
- External plugins: reject **id collision** with built-in plugins; schtasks TR rejects unsafe exe path characters.

### Fixed / reliability
- Sanitize settings on load: clamp monitoring interval, backup count, UI culture, profile timer targets and pre-rendered frames.
- Profile apply clamps `TimerTargetMs` before `NtSetTimerResolution` tuning.

### Changed
- Product version **1.2.2**.

---

## [1.2.1] — 2026-07-18

### Fixed
- **Update check** prefers **github.com Atom** first (many networks poison/block `api.github.com`); API is optional fallback with known-good IPs.
- Update errors are **localized** (EN/RU) via stable error codes — no more Russian Win32 socket text on the English UI.
- **Settings line no longer shows a fake network error** after a successful “up to date” check (`LocalizeUpdateError` only when `Error`/`ErrorCode` set).
- Failed check no longer shows «You are up to date» next to a real error.
- **Latency chart** no longer freezes while «Check for updates» runs (`busy` + `keepChart`).
- Failed update checks no longer throttle the next startup check (6 h).
- Check reply no longer fails the whole flow if `BuildUiState` throws after a successful check.

### Changed
- Product version **1.2.1**.
- Constructed Setup download URLs when asset list is unavailable: `AntiLagNext-Setup-{ver}-win-{rid}.exe`.

---

## [1.2.0] — 2026-07-18

### Added
- **In-app auto-update** from GitHub Releases (check + silent Inno Setup install).
- Settings → Updates: check / install / open Releases; dashboard banner when a newer version exists.
- Startup update check (throttled, 6 h) without blocking UI.

### Fixed
- Inno reinstall no longer prompts «folder already exists» (`DirExistsWarning=no`, `DisableDirPage=auto`).

### Changed
- Product version **1.2.0**.
- Stack cleanup: removed one-off **Python** scripts, optional **C++/CMake** native stub, legacy **WPF** `AntiLagNext.App` (Photino UI is the only GUI).

### Notes
- Silent update works for **Program Files** installs; portable zip opens Releases for manual Setup.
- Silent flags: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS`.

---

## [1.1.0] — 2026-07-17

### Added
- **System health** Photino page (Audit + Drift): scan latency registry settings and desired-state drift.
- IPC commands: `getDrift`, `getAudit`, `reapplyDrift`, `fixAudit` (safe-only or all CanFix findings).
- Compact `drift` / `audit` summaries on `getState` for nav badges.
- i18n keys (`nav.health`, `health.*`, audit titles) for EN and RU.
- **TweakCatalog** (Winrift-inspired latency registry pack) applied with Gaming / Max / Office profiles.
- **Phase 3 tweaks:** TCP Nagle hygiene (`TcpAckFrequency`, `TCPNoDelay`), input sticky/toggle keys, mouse accel off.
- **NVIDIA** multi-path `RmGpsPsEnablePerCpuCoreDpc` on Gaming/Max when GPU low-latency is on.
- **Max Performance:** optional GPU preemption off (aggressive; reboot may be required).
- Chart **fixed Y-scale** rungs (200 / 500 / 1000 / 2000 / 5000 µs); click scale to lock.
- Area-line chart rendering (smoothed display only).
- QA smoke checklist: `docs/QA_SMOKE.md`
- i18n parity script: `scripts/check-i18n.ps1`
- Unit tests: registry snapshot on missing values; Infrastructure.Tests project.

### Fixed
- **Peak (1 min)** no longer climbs forever to absurd values (rolling 60 s buckets; UI no forever `Math.max`).
- Registry snapshot no longer throws on **missing** values (`GetValueKind`) — Health Fix / catalog apply.
- Partial catalog apply reports success with skip details; UI shows last Health result panel.
- Chart Y-axis no longer continuously re-zooms (discrete rungs + hysteresis).

### Changed
- Product version **1.1.0** (`Directory.Build.props`, installer, UI label).
- `ext.registry.tweaks`: NetworkThrottling default **off** (owned by catalog) to avoid dual-writes.
- Setup package name: `AntiLagNext-Setup-1.1.0-win-x64.exe` (and other arch).

### Docs
- Architecture notes for TweakCatalog, DesiredState, Drift, Audit, health IPC, peak metric, chart scale.

---

## [1.0.3] — 2026-07-15

### Fixed
- Sidebar brand logo: removed the square frame behind the mark; logo uses transparent background and `object-fit: contain`.
- Brand row alignment with navigation (consistent padding and spacing).
- Application version shown in the UI title bar is **1.0.3** (no longer a static `1.0` label).
- Product version unified across assembly, Photino UI, CLI, and Inno Setup installer.

### Changed
- Product version set to **1.0.3** via `Directory.Build.props` (`Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion`).
- Setup package naming includes version, e.g. `AntiLagNext-Setup-1.0.3-win-x64.exe`.

### Notes
- Install over a previous build (or uninstall first) so Programs and Files pick up **1.0.3**.
- Close any running `AntiLagNext.exe` before installing if files are locked.

---

## [1.0.2] — 2026-07-10

### Fixed
- **Active profile** title follows the in-app RU/EN language toggle (not the OS/host culture).
  - Russian UI: e.g. «Игровой»; English UI: e.g. «Gaming».
- English language pack: profile card labels no longer leak Cyrillic when **EN** is selected.
- **Auto-apply on startup** no longer re-enables optimization after the user clicked **Disable** / **Reset all**.
  - Auto-apply only if the user previously enabled optimization **and** left it ON (`ActiveState`).
  - Clear auto-apply preference after a successful revert; log skip reasons.

### Added
- Settings **schema v2** migration: legacy Russian built-in profile names → stable English keys; ensure Max Performance profile exists.
- Localization for known engine / log messages (ProfileService / settings flows).
- Accessibility: `aria-live` / `aria-label` on the Active profile card.
- Photino i18n smoke tests (EN pack must not contain Cyrillic profile labels).
- Self-contained Setup helper script (`scripts/build-setup-selfcontained.ps1`).
- `docs/IMPROVEMENTS.md` backlog notes.

---

## [1.0.1] — 2026-07-10

### Added
- **Inno Setup** installers (`Setup.exe`) for Windows (multi-arch release pipeline).
- CI: install Inno Setup from the official portable release (not Chocolatey).
- Release packaging for Setup artifacts on tags `v*`.

### Fixed
- CI: Inno Setup (`ISCC`) help exit code no longer fails the workflow.
- Checkout / .NET setup actions updated for current runners.

### Changed
- README header/badge polish (centered product layout).

---

## [1.0.0] — 2026-07-10

First **public** release. Developer: [swd3k](https://github.com/swd3k).

### Added
- **Photino** desktop UI (WebView2) as the primary host for end users.
- **CLI** (`AntiLagNext.Cli`) for scripting: apply / revert / status (including silent modes).
- Optimization profiles: **Gaming**, **Office**, **Max Performance**.
- System tweaks via Win32 / registry (scoped):
  - Timer resolution hold
  - Power plan tuning
  - Game Mode / DVR / HAGS-related options
  - GPU low-latency registry paths (where applicable)
- **JSON backup** before mutations; optional **System Restore** point when available.
- **Reset all** / CLI `--revert` to restore prior state from backup.
- Live **scheduling-latency** chart (µs proxy — not network ping, not kernel DPC).
- Plugins panel (core + experimental stubs; experimental modules do not change the system in MVP).
- RU / EN localization packs.
- Tray minimize, optional **Start with Windows** (Task Scheduler, explicit confirmation).
- Multi-arch publish: **win-x64**, **win-x86**, **win-arm64** (UI + CLI portable zips).
- GitHub Actions: CI on push/PR; **Release** workflow on tags `v*` (publish zips / release notes).
- Security baseline: registry restore allowlist, safe service-name allowlist, elevated-by-design UAC.
- MIT license, README, SECURITY notes.

### Fixed (pre-public hardening, included in 1.0.0)
- Latency probe under load: multi-sample median/max, high-priority long-running probe thread, less UI self-noise.
- Backup path traversal guard; stricter registry restore parse / allowlist.
- Invalid power GUID / timer caps normalization (early WPF-era fixes carried into the product core).

### Notes
- Clean-room successor to the *ideas* behind AmbitiousPilots/AntiLag (**not a fork**).
- Unofficial tool; not affiliated with game publishers or anti-cheat vendors.
- Builds are typically **unsigned** — SmartScreen may warn.

---

## [Unreleased] / pre-1.0.0 (development, 2026-07-09)

Internal history before the first public tag (for completeness):

### Added
- Initial solution: Core, Infrastructure, **WPF** app shell, native stub, unit + Win32 smoke tests.
- Dashboard, monitoring, profiles, backups, settings, tips views.
- Publish scripts and early Inno script.

### Changed
- Full UI redesign from the antilag-next-dashboard mockup (zinc/cyan shell, live chart, bilingual layout).
- Preparation for public GitHub (docs, banner, release workflow).

---

## Version map

| Version | Date       | Highlights |
|---------|------------|------------|
| **1.3.0** | 2026-07-18 | Trust & Clarity + polish: wizard, what-changed, true BA median, chart Y to 15k µs, Health fix recommended, diagnostics, full RU i18n |
| **1.2.2** | 2026-07-18 | Security: registry prefix boundary, update download hardening, IPC confirm reboot |
| **1.2.1** | 2026-07-18 | Atom-first update check, EN error i18n, no fake network error on success |
| **1.2.0** | 2026-07-18 | In-app auto-update, Inno silent upgrade, stack cleanup |
| **1.1.0** | 2026-07-17 | Health/Audit/Drift, TweakCatalog, Peak fix, chart scale, NVIDIA DPC |
| **1.0.3** | 2026-07-15 | Version branding, logo/UI polish, Setup name with version |
| **1.0.2** | 2026-07-10 | Profile i18n, auto-apply after Disable, settings migration |
| **1.0.1** | 2026-07-10 | Inno Setup installers + CI packaging |
| **1.0.0** | 2026-07-10 | First public Photino UI + CLI multi-arch release |

---

## Links

- Repository: https://github.com/swd3k/antilag-next  
- Releases: https://github.com/swd3k/antilag-next/releases  
- Security: [SECURITY.md](SECURITY.md)

<!-- release-notes-source -->
