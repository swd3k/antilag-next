# Plugin developer guide

## Contract

Implement `AntiLagNext.Core.Plugins.IAntiLagPlugin` in a class library targeting `net8.0-windows` (or `net8.0` if no UI Win32).

Reference only:

- `AntiLagNext.Core` (preferred)
- Do **not** reference App or Infrastructure unless necessary

## Lifecycle

1. Host discovers assemblies in `plugins/` next to `AntiLagNext.exe`
2. Instantiates public parameterless types implementing `IAntiLagPlugin`
3. `InitializeAsync` once
4. `ApplyAsync` when user enables + applies profile / plugin toggle
5. `RevertAsync` on Reset all / disable

## Rules

- No spin-wait loops
- No unbounded allocations on apply path
- Prefer reversible registry/power changes
- Report honest `LatencyImpact` (do not claim Reflex-class gains for registry-only tweaks)
- Respect `PluginApplyContext.CancellationToken`

## Sample layout

```
plugins/
  MyVendor.AntiLag.NetBoost.dll
i18n/
  en.json   # optional merge keys plugin.my.*
  ru.json
```

## UI

Return `PluginUiDescriptor` list from `GetUiDescriptors()`:

- Toggle / Int / Enum fields
- `TooltipKey` for i18n
- Host renders them on **Plugins** page without knowing your types
