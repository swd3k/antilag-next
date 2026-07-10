<p align="center">
  <img src="docs/assets/banner.jpg" alt="AntiLag Next — Before (red latency) vs After (green latency)" width="100%" />
</p>

<h1 align="center">AntiLag Next</h1>

<p align="center">
  <strong>Open-source Windows latency &amp; performance optimizer</strong><br/>
  One click · Safe rollback · Honest metrics
</p>

<p align="center">
  <a href="https://github.com/swd3k/antilag-next"><img src="https://img.shields.io/badge/github-swd3k%2Fantilag--next-181717?logo=github" alt="GitHub" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT" /></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet" alt=".NET 8" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6?logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/size-≤%205%20MB%20FDD-orange" alt="Size" />
</p>

<p align="center">
  <b>Developer:</b> <a href="https://github.com/swd3k"><strong>swd3k</strong></a>
  · <b>License:</b> MIT © 2026 swd3k
</p>

---

## What it is

**AntiLag Next** is a clean-room, open-source successor to the *ideas* behind [AmbitiousPilots/AntiLag](https://github.com/AmbitiousPilots/AntiLag) — **not a fork** (original is closed binary, CC BY-NC-ND).

| | **Before** | **After** |
|---|------------|-----------|
| Status | Idle / not optimized | Optimized |
| Chart | High / unstable (red zone) | Lower / stable (green zone) |
| Action | — | **Enable AntiLag Next** |

> The µs chart is a **scheduling-latency proxy** — not kernel DPC and not network ping. Numbers help you compare *before vs after*, not absolute input lag.

---

## Highlights

| Area | What you get |
|------|----------------|
| **Quick Boost** | One-click Gaming / Office / Max Performance |
| **Core** | Timer resolution, power plan, core parking, Game Mode / HAGS, GPU low-latency registry |
| **Safety** | JSON backup, registry allowlist, incomplete-apply recovery, optional restore point |
| **Tray** | Minimize to tray · optional Windows logon (only after **confirm**) |
| **CLI** | `--apply gaming --silent` · `--revert` · `--status` |
| **Size** | Portable UI ~**1.5 MB** (framework-dependent) |

📚 [Architecture](docs/ARCHITECTURE.md) · [Plugins](docs/PLUGINS.md) · [Security](SECURITY.md) · [Contributing](CONTRIBUTING.md)

---

## Requirements

- Windows **10 20H2+** / **11** (x64 recommended; also **x86** / **ARM64** builds)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually preinstalled)
- **Administrator** rights (UAC)

---

## Run (users)

1. Download a release or build from source (`scripts\publish.ps1`).
2. Run **`AntiLagNext.exe` as Administrator**.
3. Read the short onboarding → press **Enable AntiLag Next**.
4. Optionally confirm **Start with Windows** and/or reboot.
5. If anything feels wrong → **Reset all**.

| CPU | Folder |
|-----|--------|
| Intel / AMD 64-bit | `dist/AntiLagNext-win-x64` or `dist/AntiLagNext` |
| 32-bit | `dist/AntiLagNext-win-x86` |
| ARM64 | `dist/AntiLagNext-win-arm64` |

---

## Build (developers)

```powershell
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release

# Shipping UI (Photino + WebView2)
dotnet run --project src\AntiLagNext.Ui -c Release

# CLI
dotnet run --project src\AntiLagNext.Cli -c Release -- --status
```

### Publish

```powershell
# win-x64 only
.\scripts\publish.ps1

# all Windows CPUs: x64 + x86 + ARM64 (+ zip)
.\scripts\publish-all.ps1

# hard suite: restore, build, tests, publish, size gate
.\scripts\hard-test.ps1
```

Self-contained (includes runtime, much larger):

```powershell
.\scripts\publish-all.ps1 -SelfContained
```

---

## Repository layout

```
├── AntiLagNext/                 # .NET solution
│   ├── src/AntiLagNext.Ui       # ★ Photino UI (shipping)
│   ├── src/AntiLagNext.Cli      # Console frontend
│   ├── src/AntiLagNext.Core
│   ├── src/AntiLagNext.Infrastructure
│   ├── src/AntiLagNext.App      # Legacy WPF (not built by default)
│   └── tests/
├── docs/                        # Architecture, plugins, banner
├── scripts/                     # publish / hard-test
├── installer/                   # Inno Setup (optional)
├── LICENSE                      # MIT © swd3k
└── README.md
```

---

## Safety

- Experimental plugins are **MVP stubs** (toggles disabled — they do **not** change the system).
- **Reset all** / `--revert` restores from backup when possible.
- Autostart = Task Scheduler job, **only after you confirm**.
- Higher power draw / heat under High Performance + high timer resolution is expected.

---

## Disclaimer

**Use at your own risk.** The developer (**swd3k**) is not liable for instability, data loss, or hardware stress. Some registry / service changes may need a reboot.

---

## License

[MIT](LICENSE) — **Copyright © 2026 [swd3k](https://github.com/swd3k)**
