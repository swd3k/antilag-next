# Changelog

All notable changes to **AntiLag Next** are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/).  
Versioning follows [Semantic Versioning](https://semver.org/).

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
| **1.0.3** | 2026-07-15 | Version branding, logo/UI polish, Setup name with version |
| **1.0.2** | 2026-07-10 | Profile i18n, auto-apply after Disable, settings migration |
| **1.0.1** | 2026-07-10 | Inno Setup installers + CI packaging |
| **1.0.0** | 2026-07-10 | First public Photino UI + CLI multi-arch release |

---

## Links

- Repository: https://github.com/swd3k/antilag-next  
- Releases: https://github.com/swd3k/antilag-next/releases  
- Security: [SECURITY.md](SECURITY.md)
