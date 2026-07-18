# AntiLag Next — Architecture (Core + Plugins)

Author: [swd3k](https://github.com/swd3k) · Repo: https://github.com/swd3k/antilag-next

## Goal

Минимальное **ядро** (timer / power / safety / monitoring / profile apply) + **подключаемые модули** (сеть, input hygiene, game hooks, experimental).  
Снижение input/system latency **без инжекта в процессы игр** (MIT clean-room, безопасный откат).

## Scope honesty (2023–2025 techniques)

| Technique | In AntiLag Next? | Why |
|-----------|------------------|-----|
| `NtSetTimerResolution` + hold process | **Core** | Real scheduling jitter win |
| Power plan / min CPU / ASPM / core parking | **Core** | C-state / wake latency |
| Game Mode / DVR / HAGS / GPU LLM registry | **Core** | Documented registry paths |
| QPC / waitable-timer probe (µs proxy) | **Core** | Measurement, not magic |
| Process priority boost while monitoring | **Core** | Probe self-noise |
| Mouse accel off / pointer precision | **Plugin: input** | Raw-ish path without game inject |
| DSCP/QoS policy tags (best-effort) | **Plugin: network** | No userspace UDP stack for games |
| CoDel/PIE, full UDP reassembly | **Out** | Needs kernel/driver or exclusive stack |
| NVIDIA Reflex / JIT present / swapchain queue | **Partial** | Registry + docs; true Reflex needs game SDK |
| Predictive input into game engine | **Out** | Requires inject / overlay cheat risk |
| Frame pacing inside DX/Vulkan game | **Out** | Not a system tool without inject |

**Rule:** if gain &lt; ~1% or unstable → do not ship as default ON.

## Layers

```
AntiLagNext.Ui           Photino (WebView2) UI — shipping host (≤5 MB FDD)
AntiLagNext.Cli          Console: --apply / --revert / --status
AntiLagNext.Infrastructure  Managers, PluginHost, safety, EngineBootstrap, UpdateService
AntiLagNext.Core         Models, contracts, plugin interfaces (no Win32)
plugins/*.dll            Optional external IAntiLagPlugin assemblies
```

**Stack (1.2.0):** C# + Photino HTML/JS + PowerShell (build) + Inno Setup. No Python, no optional native C++ DLL required.

## Core (always present)

- `ITimerManager`, `IPowerManager`, `ICoreParkingManager`
- `IGameModeManager`, `IGpuManager`, `IMemoryManager`
- `ISafetyService` / `IBackupService` (restore point + JSON)
- `IProfileService` (orchestrates apply/revert)
- `IMonitoringService` (HiPri probe, fixed buffers)
- `IPluginHost` / `IPluginCatalog` (load built-in + external)
- `IDesiredStateStore` / `IDriftService` / `IAuditService` (catalog latency tweaks)

## TweakCatalog, Drift, Audit

Latency registry tweaks live in a static **TweakCatalog** (`AntiLagNext.Infrastructure.Tweaks`):

| Piece | Role |
|-------|------|
| `TweakCatalog` | Declarative `TweakDefinition` list (id, hive/path, desired value, risk, profile tags). `ForProfile(kind)` returns Safe+Moderate entries for Gaming / Max / Office subset. |
| `RegistryTweakEngine` | Applies catalog rows under an open backup session; path allowlist; upserts **desired state**. |
| `IDesiredStateStore` | Persisted expected registry values after apply (JSON under app data). |
| `IDriftService` | `Scan()` compares desired/catalog vs live registry → `DriftEntry` (`Ok` / `Drifted` / `Missing`). `ReapplyDriftedAsync(sessionId)` rewrites drifted/missing catalog values. |
| `IAuditService` | Read-only `Scan()` of known latency keys + active-state note → `AuditFinding` (severity, optional `SuggestedTweakId`, `CanFix`). No built-in Fix API — UI/host applies via `RegistryTweakEngine` + `ISafetyService`. |

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

Categories: `Power`, `Gpu`, `Network`, `Input`, `Game`, `Experimental`.

### Built-in plugins (v1)

| Id | Role |
|----|------|
| `core.timer` | Facade over timer (documented for catalog; applied by ProfileService) |
| `core.power` | Facade over power/parking |
| `core.gpu` | Facade over HAGS/LLM |
| `ext.network.qos` | Best-effort DSCP / multimedia class (optional) |
| `ext.input.pointer` | Disable enhance pointer precision (optional) |
| `ext.process.priority` | Optional High priority for AntiLag process when optimizing |

External DLLs: `{AppBase}/plugins/*.dll` loaded via `AssemblyLoadContext` (collectible later).

## Hot paths

Monitoring loop:

- Dedicated thread, `Highest` priority
- Fixed `double[]` probe buffer (no per-tick alloc)
- Waitable-timer sleep (not `Task.Delay`)
- UI throttle ≤ 8–10 Hz
- Waveform: `OnRender` only

Forbidden in probe path: `Process.GetProcesses`, LINQ materialization, logging every sample, UI `Invoke` (use `BeginInvoke`).

### Peak (1 min) metric

- **Not** a growing all-time max.
- Photino host (`Program.OnSample`): 60 per-second buckets → max of buckets still inside the window.
- Samples sanitized (NaN/Inf rejected; values &gt; 10 ms clamped as probe glitches).
- UI displays host `p` / `peakUs` as-is (no client-side forever `Math.max`).

## UI

- Design tokens (zinc/cyan), Photino WebView2 (shipping) + legacy WPF reference
- **i18n**: JSON language packs `wwwroot/i18n/{culture}.json` (Photino) / `i18n/` (WPF)
- Themes: Dark / Light / System
- **Plugins page**: list + enable toggles + plugin-contributed setting rows
- **System health page**: audit findings + drift table; Refresh / Fix safe / Fix all / Reapply drifted
- Tooltips: latency impact (High / Medium / Low / Experimental)

## Roadmap

| Phase | Deliverable |
|-------|-------------|
| **P0** (this) | Contracts, host, built-in ext plugins, i18n RU/EN, Plugins UI, hot-path buffers, docs |
| **P1** | ProfileService fully plugin-driven apply pipeline; settings schema per plugin |
| **P2** | Collectible ALC unload; signed plugins; sample external plugin project |
| **P3** (partial) | Winrift catalog expand (network Nagle, input); NVIDIA per-CPU DPC; Max preemption off; peak metric fix |
| **Later** | Optional: ETW DPC/ISR viewer; waitable swapchain helper app (not inject) |
| **Never** | Game memory write, anti-cheat bypass, hidden network MITM |

## Safety

Every system change: `ISafetyService.BeforeChangesAsync` → JSON snapshots → optional restore point.  
Plugins must use `IPluginServices.Backup` / not write HKLM without snapshot hooks when possible.
