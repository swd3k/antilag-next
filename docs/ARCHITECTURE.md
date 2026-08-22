# AntiLag Next — Architecture (Core + Plugins)

Author: [swd3k](https://github.com/swd3k) · Repo: https://github.com/swd3k/antilag-next  
**Current as of 1.4.1.**

## Goal

Minimum **core** (timer / power / safety / monitoring / profile apply) + **plug-ins** (network, input hygiene, game hooks, experimental).  
Reducing input/system latency **without injecting into game processes** (MIT clean-room, safe rollback).

## Scope honesty (through 1.4.1)

| Technique | In AntiLag Next? | Why |
|-----------|------------------|-----|
| `NtSetTimerResolution` + `timeBeginPeriod` hold | **Core** | Process-local on Win11 22H2+ unless `GlobalTimerResolutionRequests` |
| `GlobalTimerResolutionRequests` (catalog, reboot) | **Core** | Games inherit the timer hold on Win11 22H2+ |
| Power plan / min CPU / ASPM / core parking | **Core** | AC-only writes; DC core parking capped at 50% |
| Game Mode / DVR / HAGS / GPU LLM registry | **Core** | Documented registry paths; **HAGS off by default** (1.4.0) |
| QPC / waitable-timer probe (µs proxy) | **Core** | Scheduling-latency proxy — not kernel DPC/ISR, not ping |
| Probe thread priority | **Core** | Dedicated thread; whole-process boost is **not** used in chart mode (starves WebView2) |
| Mouse accel / pointer precision | **Out** | No `ext.input.pointer` plugin ships |
| DSCP/QoS policy tags (best-effort) | **Plugin: network** | No userspace UDP stack for games |
| CoDel/PIE, full UDP reassembly | **Out** | Needs kernel/driver or exclusive stack |
| NVIDIA Reflex / JIT present / swapchain queue | **Partial** | Registry + docs; true Reflex needs game SDK |
| Predictive input into game engine | **Out** | Requires inject / overlay cheat risk |
| Frame pacing inside DX/Vulkan game | **Out** | Not a system tool without inject |

**Rule:** if gain < ~1% or unstable → do not ship as default ON.

## Layers

```
AntiLagNext.Ui           Photino (WebView2) UI — shipping host (≤5 MB FDD)
AntiLagNext.Cli          Console: --apply / --revert / --status
AntiLagNext.Infrastructure  Managers, PluginHost, safety, EngineBootstrap, UpdateService
AntiLagNext.Core         Models, contracts, plugin interfaces (no Win32)
plugins/*.plugin.dll     Optional external IAntiLagPlugin (AllowExternalPlugins, default off)
```

**Stack (1.2.0+):** C# + Photino HTML/JS + PowerShell (build) + Inno Setup. No runtime Python. `scripts/process-brand.py` is brand-asset import only. Native C++ stub removed in 1.2.0.

## Core (always present)

- `ITimerManager`, `IPowerManager`, `ICoreParkingManager`
- `IGameModeManager`, `IGpuManager`, `IMemoryManager`
- `ISafetyService` / `IBackupService` (restore point + JSON; 1.4.1: `LoadBySessionId`)
- `IProfileService` (orchestrates apply/revert)
- `IMonitoringService` (HiPri **thread**, fixed buffers)
- `IPluginHost` / `IPluginCatalog` (load built-in + optional external)
- `IDesiredStateStore` / `IDriftService` / `IAuditService` (catalog latency tweaks)
- `SystemNative.Exe` — System32-qualified `powercfg` / `schtasks` / `ipconfig` / `shutdown`

## TweakCatalog, Drift, Audit

Latency registry tweaks live in a static **TweakCatalog** (`AntiLagNext.Infrastructure.Tweaks`):

| Piece | Role |
|-------|------|
| `TweakCatalog` | Declarative `TweakDefinition` list (id, hive/path, desired value, risk, profile tags). `ForProfile(kind)` returns Safe+Moderate entries for Gaming / Max / Office subset. |
| `RegistryTweakEngine` | Applies catalog rows under an open backup session; path allowlist; upserts **desired state**. |
| `IDesiredStateStore` | Persisted expected registry values after apply (JSON under app data). |
| `IDriftService` | `Scan()` compares desired/catalog vs live registry → `DriftEntry` (`Ok` / `Drifted` / `Missing`). `ReapplyDriftedAsync(sessionId)` rewrites drifted/missing catalog values. |
| `IAuditService` | Read-only `Scan()` of known latency keys + active-state note → `AuditFinding` (severity, optional `SuggestedTweakId`, `CanFix`). No built-in Fix API — UI/host applies via `RegistryTweakEngine` + `ISafetyService`. |
| `RegistryPathPolicy` | Prefix allowlist **and** denied value names (`ImagePath`, `NameServer`, `AppInit_DLLs`, `Debugger`, …). |

Profile apply path: after power/timer/game-mode, `ProfileService` calls `TweakCatalog.ForProfile` → `RegistryTweakEngine.ApplyAsync` on the same safety session.

### Photino UI IPC (System health page)

| cmd | Path | Payload |
|-----|------|---------|
| `getDrift` | fast | `{ ok, entries[], driftedCount, total }` |
| `getAudit` | fast | `{ findings[], count }` |
| `reapplyDrift` | heavy (worker) | backup session → `Drift.ReapplyDriftedAsync` → `{ success, message, state }` |
| `fixAudit` | heavy (worker) | `safeOnly` → CanFix findings → catalog apply → `{ success, fixedCount, state }` |

`BuildUiState` also exposes compact badges: `drift: { driftedCount, total }`, `audit: { issueCount }` (excludes active-state heartbeat).

## Plugin model

```csharp
IAntiLagPlugin
  Id, NameKey, DescriptionKey, Version, Category, IsBuiltIn
  InitializeAsync(IPluginServices)
  ApplyAsync(PluginApplyContext)   // after safety session opened
  RevertAsync()
  GetUiDescriptors() → settings for Plugins page
```

Categories: `Core`, `Power`, `Gpu`, `Network`, `Input`, `Game`, `Experimental`.

### Built-in plugins (1.4.1)

| Id | Role | Default |
|----|------|---------|
| `core.timer` | Facade; applied by ProfileService | on (core) |
| `core.power` | Facade over power/parking (AC-only writes) | on (core) |
| `core.gpu` | Facade over HAGS/LLM (HAGS off in Gaming/Max) | on (core) |
| `ext.network.qos` | Best-effort DSCP / MMCSS | on |
| `ext.network.hygiene` | `ipconfig /flushdns` via System32 | on |
| `ext.registry.tweaks` | Extra catalog-adjacent registry (allowlisted) | on |
| `ext.process.priority` | High priority for **listed game exes only** — never AntiLag/WebView2 | **off** |
| `ext.services.safe` | Opt-in SysMain / DiagTrack → Manual (allowlist) | off |
| `exp.msi` / `exp.irq.affinity` / `exp.driver.blacklist` | Stubs, never in Quick Boost | off |

External: `{AppBase}/plugins/*.plugin.dll` via collectible `AssemblyLoadContext` **only if** `AllowExternalPlugins=true` (default **false**). Random `*.dll` is not loaded.

## Hot paths

Monitoring loop:

- Dedicated thread (`Highest` when charting; not whole-process `PriorityClass`)
- Fixed `double[]` probe buffer (no per-tick alloc)
- Waitable-timer sleep (not `Task.Delay`)
- UI throttle ≤ 8–10 Hz
- Chart: Photino JS canvas from `getMetrics` (no WPF `OnRender`)

Forbidden in probe path: `Process.GetProcesses`, LINQ materialization, logging every sample, synchronous UI waits.

### Peak (1 min) metric

- **Not** a growing all-time max.
- Photino host (`Program.OnSample`): 60 per-second buckets → max of buckets still inside the window.
- Samples sanitized (NaN/Inf rejected; values > 10 ms clamped as probe glitches).
- UI displays host `p` / `peakUs` as-is (no client-side forever `Math.max`).

## UI

- Design tokens (zinc/cyan), Photino WebView2 (shipping)
- **i18n**: JSON language packs `wwwroot/i18n/{culture}.json`
- Themes: Dark / Light / System
- **Plugins page**: list + enable toggles + plugin-contributed setting rows
- **System health page**: audit findings + drift table; Refresh / Fix recommended / Fix safe / Fix all / Reapply drifted
- Tooltips: latency impact (High / Medium / Low / Experimental)

## Roadmap

| Phase | Status | Deliverable |
|-------|--------|-------------|
| **P0** | Shipped | Contracts, host, built-in ext plugins, i18n RU/EN, Plugins UI, hot-path buffers, docs |
| **P1** | Partial | ProfileService still owns timer/power/gpu; ext plugins apply after |
| **P2** | Partial | Collectible ALC exists; signed plugins + sample project **not** shipped |
| **P3** | **Shipped in 1.4.0** | Win11 global timer; AC-only power; HAGS/RAM-trim off by default |
| **1.4.1** | **Shipped** | SHA256 Setup verify, System32 native tools, session-scoped crash restore, chip logo |
| **Later** | Open | Optional ETW DPC/ISR viewer; waitable swapchain helper (not inject); Authenticode |
| **Never** | — | Game memory write, anti-cheat bypass, hidden network MITM |

## Safety

Every system change: `ISafetyService.BeforeChangesAsync` → JSON snapshots (session-prefixed) → optional restore point.

- Crash recovery restores **only that apply-session** backup (`LoadBySessionId`), not “newest JSON in AppData”.
- Restore refuses persistence values even under allowlisted key prefixes.
- Native tools run from `%SystemRoot%\System32` (no PATH).
- In-app Setup requires a matching SHA256 from the same release `SHA256SUMS.txt`; PE probe fails closed; prerelease tags are not treated as shipping versions.
- Plugins must use `IPluginServices.Backup` / not write HKLM without snapshot hooks when possible.
