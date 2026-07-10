# AntiLag Next

Modern open-source Windows latency / gaming performance tool — a clean-room successor to the ideas behind [AmbitiousPilots/AntiLag](https://github.com/AmbitiousPilots/AntiLag) (**not a fork**; original is closed binary, CC BY-NC-ND).

| | |
|---|---|
| **Developer** | **[swd3k](https://github.com/swd3k)** |
| Repository | [github.com/swd3k/antilag-next](https://github.com/swd3k/antilag-next) |
| Stack | **C# / .NET 8** · **Photino (WebView2)** UI · CLI |
| License | **MIT** · Copyright © 2026 **swd3k** |
| OS | Windows 10 20H2+ / 11 **x64** (administrator UAC) |
| Portable size | **≤ 5 MB** framework-dependent (`dist/AntiLagNext` ~1.6 MB) |

## Features

- **Quick Boost** — one-click Gaming / Office / Max Performance profiles  
- **Core** — timer resolution, power plans, core parking, Game Mode / HAGS, GPU low-latency registry  
- **Plugins** — network hygiene, registry tweaks, process priority, safe services; experimental modules are **stubs** (UI disabled)  
- **Safety** — JSON backup, registry path allowlist, incomplete-apply recovery, optional restore point  
- **Tray + autostart** — minimize to tray; Windows logon task only after **explicit confirm**  
- **CLI** — `--apply gaming --silent`, `--revert`, `--status`  
- **Monitoring** — scheduling-latency **proxy** (not kernel DPC / not network ping)

Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · Plugins: [docs/PLUGINS.md](docs/PLUGINS.md)

## Prerequisites

- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (framework-dependent publish)  
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually preinstalled on Windows 10/11)  
- **Administrator** elevation (UAC)

## Quick start (developers)

```powershell
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release

# Shipping UI (Photino)
dotnet run --project src\AntiLagNext.Ui -c Release

# CLI
dotnet run --project src\AntiLagNext.Cli -c Release -- --status
dotnet run --project src\AntiLagNext.Cli -c Release -- --apply gaming --silent
dotnet run --project src\AntiLagNext.Cli -c Release -- --revert --silent
```

Hard test suite (restore, build, unit, smoke, publish, size gate):

```powershell
.\scripts\hard-test.ps1
```

## Publish portable

```powershell
.\scripts\publish.ps1
# → dist\AntiLagNext\       (UI)
# → dist\AntiLagNext-cli\   (CLI)
```

Self-contained (larger):

```powershell
.\scripts\publish.ps1 -SelfContained -AllowOversize
```

Portable data next to the exe: create empty file `AntiLagNext.portable` (uses `./data`).

## Repository layout

```
├── AntiLagNext/                 # .NET solution
│   ├── src/AntiLagNext.Ui       # Photino UI (shipping)
│   ├── src/AntiLagNext.Cli
│   ├── src/AntiLagNext.Core
│   ├── src/AntiLagNext.Infrastructure
│   ├── src/AntiLagNext.App      # legacy WPF (not built by default)
│   └── tests/
├── docs/
├── scripts/                     # publish.ps1, hard-test.ps1, …
├── installer/                   # Inno Setup (optional)
├── LICENSE                      # MIT © swd3k
└── README.md
```

## Safety notes

- Experimental plugins are **MVP stubs** and cannot be enabled as real optimizers.  
- Use **Reset all** / `--revert` if something feels wrong.  
- Higher power use and temperatures are expected under High Performance + high timer resolution.  
- Autostart creates a Task Scheduler job **only after you confirm**.

## Disclaimer

**Use at your own risk.** The author (**swd3k**) is not liable for system instability, data loss, or hardware stress. Some registry / service changes may need a reboot.

## License

MIT — see [LICENSE](LICENSE).  

**Copyright © 2026 [swd3k](https://github.com/swd3k)**
