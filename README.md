<p align="center">
  <img src="docs/assets/banner.jpg" alt="AntiLag Next — Before (red latency) vs After (green optimized)" width="100%">
</p>

<br>

<h1 align="center">AntiLag Next</h1>

<p align="center">
  Reduce Windows <strong>scheduling latency</strong> with one-click profiles, live µs monitoring, Health audit/drift, and full reset — open source, no inject, no telemetry.
</p>

<p align="center">
  <a href="https://github.com/swd3k/antilag-next/releases/latest"><img alt="version" src="https://img.shields.io/github/v/release/swd3k/antilag-next?style=flat-square&label=version" /></a>
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows-lightgrey?style=flat-square" />
  <img alt="license" src="https://img.shields.io/badge/license-MIT-brightgreen?style=flat-square" />
  <img alt="status" src="https://img.shields.io/badge/status-release-brightgreen?style=flat-square" />
  <a href="https://github.com/swd3k"><img alt="author" src="https://img.shields.io/badge/author-swd3k-24292e?style=flat-square&logo=github&logoColor=white" /></a>
  <img alt="C%23" src="https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white" />
  <img alt="JavaScript" src="https://img.shields.io/badge/JavaScript-F7DF1E?style=flat-square&logo=javascript&logoColor=black" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet&logoColor=white" />
</p>

<p align="center">
  Developer: <a href="https://github.com/swd3k">swd3k</a>
  ·
  <a href="https://github.com/swd3k/antilag-next/releases">Releases</a>
  ·
  <a href="CHANGELOG.md">Changelog</a>
  ·
  <a href="LICENSE">MIT</a>
</p>

<p align="center">
  <a href="https://github.com/swd3k/antilag-next/releases/latest/download/AntiLagNext-Setup-1.4.0-win-x64.exe">
    <img alt="Download Setup win-x64" src="https://img.shields.io/badge/⬇%20Download-Setup%20win--x64-0969DA?style=for-the-badge&logo=github&logoColor=white" />
  </a>
  &nbsp;
  <a href="https://github.com/swd3k/antilag-next/releases/latest">
    <img alt="All releases" src="https://img.shields.io/badge/All%20releases-gray?style=for-the-badge" />
  </a>
</p>

<p align="center">
  <sub>Latest: <strong>1.4.0</strong> · Direct download: <a href="https://github.com/swd3k/antilag-next/releases/latest/download/AntiLagNext-Setup-1.4.0-win-x64.exe"><code>AntiLagNext-Setup-1.4.0-win-x64.exe</code></a>
  · portable ZIPs and other arch on <a href="https://github.com/swd3k/antilag-next/releases">GitHub Releases</a></sub>
</p>

---

> [!NOTE]
> **Unofficial open-source** tool. Not affiliated with any game publisher or anti-cheat vendor.  
> Clean-room successor to the *ideas* behind [AmbitiousPilots/AntiLag](https://github.com/AmbitiousPilots/AntiLag) (**not a fork**).  
> **Use at your own risk.** Requires **Administrator** rights (UAC).

Desktop app for **Windows 10 / 11** that applies carefully scoped system tweaks (timer resolution, power plan, Game Mode / HAGS, GPU low-latency registry, curated latency pack) to reduce **scheduling latency**. One-click **Enable**, **What changed** summary, live chart **Before/After**, **Health** audit/drift, full **Reset all**, optional tray + autostart, in-app update, and local diagnostics export.

---

> [!CAUTION]
> ### 🚫 FAKES
> I do **not** run any other pages, groups, Telegram, or YouTube channels for this project.  
> The **only** official source is **this GitHub repository**.  
> Anything distributed under my name outside this repo is a **FAKE**.

> [!WARNING]
> ### 🛡️ ANTIVIRUS & SmartScreen
> AntiLag Next requests **Administrator (UAC)** and may change power settings, registry keys, and timer resolution. Security software or Windows SmartScreen may flag the unsigned build.  
> This is **not a virus**: the source is fully open — review it or build it yourself.
>
> The executable is **not signed** with a paid code-signing certificate, so you may see *“Windows protected your PC”*. If you trust the source (and verified the download from GitHub Releases), choose **More info → Run again**. Add the app folder to antivirus exclusions if needed.

> [!IMPORTANT]
> ### 🔐 What you should know
> - Prefer builds from **[Releases](https://github.com/swd3k/antilag-next/releases)** only.  
> - The µs chart is a **scheduling-latency proxy** — **not** kernel DPC and **not** network ping.  
> - On **Windows 11 22H2+** games inherit the timer hold only after `GlobalTimerResolutionRequests` **and a reboot**. The UI shows global / this-process / reboot needed.  
> - **HAGS** and working-set trim are **off by default** (they often add hitching / input lag).  
> - Experimental plugins are **MVP stubs** (disabled in the UI; they do **not** change the system).  
> - Only **one UI instance** runs at a time — a second launch focuses the existing window (including from tray).  
> - Always use **Reset all** if something feels wrong. Not sure? Build from source (below).

---

## ⚙️ What the app does

On **Enable AntiLag Next**, the app applies the selected profile (**Gaming** / **Office** / **Max Performance**) via Win32 APIs and registry paths: timer resolution hold (`timeBeginPeriod` + `NtSetTimerResolution`; Win11 global key + reboot), power scheme tuning **on AC only**, Game Mode / DVR, GPU low-latency where applicable, a curated latency registry pack, and optional plugin modules (network hygiene, process priority, safe services, etc.). HAGS is not turned on unless you opt in.

After Enable you get:

- **What changed** — plain-language list of applied areas (hide/show; survives language switch).  
- **Before / After** — median µs over a real sample window (not a single point).  
- Optional reboot / autostart prompts (confirm required for autostart).

Use the **Health** page to audit key latency settings, detect **desired-state drift** after Windows updates, **Fix recommended** (Safe + reapply owned state), or Fix safe / Fix all. Changes are stored as **JSON backup** (and optional System Restore when available). **Reset all** / CLI `--revert` restores the previous state as far as the backup allows.

The live chart probes scheduling latency on a short interval so you can compare *before vs after* — not absolute input lag inside games.

---

## 🔒 Security notes

- Runs **elevated** by design — treat untrusted binaries of this class as high risk; prefer building from source.  
- Registry restore uses a **strict path allowlist** (prefix + path boundary); service changes use a **safe-name allowlist**.  
- External `*.plugin.dll` loading is **opt-in** (default off); plugin id collision rejected.  
- **Start with Windows** / **reboot** only after **explicit confirmation** in the UI.  
- In-app update downloads only official Setup URLs (canonical repo + CDN allowlist, size + PE checks).  
- Release zips/Setup include **SHA256SUMS.txt** — verify the hash before running.  
- **Diagnostics export** is local-only (redacted settings; no cloud).  
- No telemetry; crash notes stay local when written.

See [SECURITY.md](SECURITY.md) for reporting vulnerabilities.

---

## 📥 Download

Get builds from **[Releases](https://github.com/swd3k/antilag-next/releases)** (latest tag: **v1.4.0**).

### Setup installer (recommended)

| Package | Arch | Notes |
|---------|------|-------|
| [`AntiLagNext-Setup-1.4.0-win-x64.exe`](https://github.com/swd3k/antilag-next/releases/latest/download/AntiLagNext-Setup-1.4.0-win-x64.exe) | Intel / AMD 64-bit | **Installer** — *most users* |
| [`AntiLagNext-Setup-1.4.0-win-x86.exe`](https://github.com/swd3k/antilag-next/releases/latest/download/AntiLagNext-Setup-1.4.0-win-x86.exe) | 32-bit | Installer |
| [`AntiLagNext-Setup-1.4.0-win-arm64.exe`](https://github.com/swd3k/antilag-next/releases/latest/download/AntiLagNext-Setup-1.4.0-win-arm64.exe) | ARM64 | Installer |

1. Run the **Setup** `.exe` (UAC / Administrator).  
2. Finish the wizard → launch **AntiLag Next**.  
3. Read the first-run wizard → pick a profile → **Enable AntiLag Next**.  
4. If anything feels wrong → **Reset all**.

Silent in-app updates work for **Program Files** installs. Portable builds open Releases for manual Setup.

### Portable zip

| Package | Arch | Contents |
|---------|------|----------|
| `AntiLagNext-win-x64.zip` | Intel / AMD 64-bit | **UI** (`AntiLagNext.exe`) |
| `AntiLagNext-win-x86.zip` | 32-bit | UI |
| `AntiLagNext-win-arm64.zip` | ARM64 | UI |
| `AntiLagNext-cli-win-*.zip` | same RIDs | **CLI** (`AntiLagNext.Cli.exe`) |

1. Extract the zip.  
2. Run **`AntiLagNext.exe` as Administrator**.

**Runtime required (framework-dependent builds):** [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) and [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (usually preinstalled on Windows 10/11).

---

## ✨ Features

| Area | Highlights |
|------|------------|
| **Profiles** | One-click Gaming / Office / Max Performance |
| **Core tweaks** | Timer resolution (`timeBeginPeriod` + `NtSetTimerResolution`; Win11 22H2+ global via `GlobalTimerResolutionRequests` + reboot), power plan / core parking (**AC only** — battery indexes never written), Game Mode, GPU low-latency (NVIDIA per-CPU DPC on Gaming/Max). **HAGS off by default.** |
| **Catalog** | Curated latency registry pack (network, input queues, kernel/power) with backup + allowlist |
| **Health** | Audit + desired-state **drift**; **Fix recommended** / Fix safe / Fix all / Reapply |
| **Transparency** | **What changed** after Enable; **Before/After** median µs window |
| **Chart** | Live area-line; fixed Y rungs **200…15000 µs**; Peak = max of last **60 s** |
| **Updates** | Check GitHub Releases; silent Setup for Program Files installs |
| **Safety** | JSON backup + **Reset all** / CLI `--revert`; SHA256SUMS on Releases |
| **Desktop** | Tray icon; optional logon autostart (confirm); **single UI instance** |
| **Diagnostics** | Export local zip (redacted settings, audit, drift, logs) |
| **Plugins** | Built-in modules; experimental items marked **stub / soon** |
| **CLI** | `--apply`, `--revert`, `--status` |
| **i18n** | **RU** + **EN** language packs |
| **Size** | Portable UI ≈ **1.7 MB** FDD (size gate ≤ 5 MB) |

Full history: [CHANGELOG.md](CHANGELOG.md) (**1.4.0**).

---

## 🛠️ Build from source

Requires **.NET 8 SDK** on Windows.

```powershell
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release
```

```powershell
# Shipping UI (Photino + WebView2) — UAC as Admin for real tweaks
dotnet run --project src\AntiLagNext.Ui -c Release

# CLI
dotnet run --project src\AntiLagNext.Cli -c Release -- --status
```

### Publish portable + Setup installers

```powershell
# win-x64 only (portable folder + zip)
.\scripts\publish.ps1

# all Windows CPUs: x64 + x86 + ARM64 (+ zip)
.\scripts\publish-all.ps1

# Inno Setup installers (requires Inno Setup 6) — framework-dependent (~2–3 MB)
.\scripts\build-installer.ps1 -Version 1.4.0

# publish + all Setup.exe in one go
.\scripts\build-installer.ps1 -Version 1.4.0 -PublishFirst

# self-contained Setup (includes .NET runtime, larger ~60–80 MB) → *-SC.exe
.\scripts\build-setup-selfcontained.ps1 -Version 1.4.0 -Rid win-x64

# full hard suite: restore, build, tests, publish, size gate
.\scripts\hard-test.ps1
```

Self-contained portable folders only:

```powershell
.\scripts\publish-all.ps1 -SelfContained
```

Settings auto-migrate on load (schema v3): legacy Russian built-in profile names become stable English labels; Gaming/Max HAGS and RAM-trim flags reset once. The UI always localizes via language packs.

CI builds on every push to `main` and on pull requests (Core + Infrastructure + Smoke + i18n). Releases are created on tags `v*` (e.g. `v1.4.0`) with multi-arch **Setup.exe** installers, portable zips, and **SHA256SUMS.txt**.

---

## 👨‍💻 For development

```powershell
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Debug
dotnet test tests\AntiLagNext.Core.Tests\AntiLagNext.Core.Tests.csproj -c Release
dotnet test tests\AntiLagNext.Infrastructure.Tests\AntiLagNext.Infrastructure.Tests.csproj -c Release
dotnet test tests\AntiLagNext.SmokeTests\AntiLagNext.SmokeTests.csproj -c Release
```

Shipping host: **`AntiLagNext.Ui`** (Photino). Legacy WPF was removed in **1.2.0**.

i18n parity check:

```powershell
.\scripts\check-i18n.ps1
```

---

## 📂 Repository layout

```
├── AntiLagNext/                 # .NET solution
│   ├── src/AntiLagNext.Ui       # ★ Photino UI (shipping)
│   ├── src/AntiLagNext.Cli
│   ├── src/AntiLagNext.Core
│   ├── src/AntiLagNext.Infrastructure
│   └── tests/
├── docs/                        # architecture, plugins, banner, QA
├── scripts/                     # publish / installer / hard-test / i18n
├── installer/                   # Inno Setup script → Setup.exe
├── CHANGELOG.md                 # full product history (Keep a Changelog)
├── LICENSE
└── README.md
```

---

## 🔗 Useful links

- 💻 Source — https://github.com/swd3k/antilag-next  
- 📦 Releases — https://github.com/swd3k/antilag-next/releases  
- 📝 Changelog — [CHANGELOG.md](CHANGELOG.md)  
- 📐 Architecture — [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)  
- 🔌 Plugins — [docs/PLUGINS.md](docs/PLUGINS.md)  
- 🔐 Security policy — [SECURITY.md](SECURITY.md)  
- 🤝 Contributing — [CONTRIBUTING.md](CONTRIBUTING.md)

---

## ⚖️ Disclaimer

Intended for users who understand system power and registry tweaks. Changing elevated system settings is **at your own risk**. A backup is created when possible; full recovery is not guaranteed on every machine or GPO-locked PC.

The author (**swd3k**) is not liable for instability, data loss, or hardware stress.

---

## 🧩 Tech stack

`C#` · `.NET 8` · `Photino.NET` · `WebView2` · `Win32` (`ntdll` / `powrprof` / `kernel32`) · `Windows Forms` (tray)

---

## 🙏 Credits

- Latency registry tweak ideas inspired by [emylfy/Winrift](https://github.com/emylfy/Winrift) (MIT). AntiLag Next reimplements a curated Safe/Moderate subset with backup, allowlist, and desired-state drift tracking — not a fork.

## 📄 License

[MIT](./LICENSE) © 2026 [swd3k](https://github.com/swd3k)
