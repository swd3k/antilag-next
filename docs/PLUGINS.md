# Plugin developer guide

Current as of **1.4.1**. Built-in ids and apply order: [ARCHITECTURE.md](ARCHITECTURE.md).

## Contract

Implement `AntiLagNext.Core.Plugins.IAntiLagPlugin` in a class library targeting `net8.0-windows` (or `net8.0` if no UI Win32).

Reference only:

- `AntiLagNext.Core` (preferred)
- Do **not** reference App or Infrastructure unless necessary

## Lifecycle

1. Host discovers assemblies in `plugins/` next to `AntiLagNext.exe`
2. **Only** files named `*.plugin.dll` are loaded — never random `*.dll`
3. Loading is **off by default** (`AppSettings.AllowExternalPlugins = false`)
4. Instantiates public parameterless types implementing `IAntiLagPlugin`
5. `InitializeAsync` once
6. `ApplyAsync` when user enables + applies profile / plugin toggle
7. `RevertAsync` on Reset all / disable

External plugins run with the host privileges (**often Administrator**). Treat that as equivalent to installing an elevated native binary.

## Rules

- No spin-wait loops
- No unbounded allocations on apply path
- Prefer reversible registry/power changes
- Snapshot through `PluginApplyContext.BackupSessionId` / `IPluginServices.Backup` before HKLM writes
- Launch Windows tools via fully qualified `%SystemRoot%\System32\…` paths (no PATH)
- Do not write persistence values (`ImagePath`, `NameServer`, `AppInit_DLLs`, `Debugger`, …) — restore will refuse them
- Report honest `LatencyImpact` (do not claim Reflex-class gains for registry-only tweaks)
- Respect `PluginApplyContext.CancellationToken`

## Sample layout

```
plugins/
  MyVendor.AntiLag.NetBoost.plugin.dll
```

Optional i18n keys (`plugin.my.*`) go in the host packs `wwwroot/i18n/en.json` and `ru.json` until a merge API exists. There is **no** shipping sample plugin project yet (backlog).

## UI

Return `PluginUiDescriptor` list from `GetUiDescriptors()`:

- Toggle / Int / Enum fields
- `TooltipKey` for i18n
- Host renders them on **Plugins** page without knowing your types
- Toggles are bound via `data-id` listeners (ids are not interpolated into inline `onchange` HTML)
