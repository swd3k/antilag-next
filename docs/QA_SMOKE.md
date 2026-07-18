# AntiLag Next — Smoke / Regression checklist

**Product:** AntiLag Next (Windows, elevated)  
**Target:** ≤ 20 minutes per build  
**Machines:** Win11 x64 (primary), Win10 22H2 optional  
**Build:** `dotnet build AntiLagNext/AntiLagNext.sln -c Release`  
**Tests:** `dotnet test AntiLagNext/AntiLagNext.sln -c Release` must be green first

| Field | Value |
|-------|--------|
| Build / commit | _______________ |
| Tester | _______________ |
| Date | _______________ |
| OS | _______________ |
| GPU | NVIDIA / AMD / Intel / none |
| Result | PASS / FAIL |

---

## Preflight

- [ ] Close all `AntiLagNext.exe` instances
- [ ] `dotnet test` → 0 failed
- [ ] Launch **as Administrator** from  
  `AntiLagNext\src\AntiLagNext.Ui\bin\Release\net8.0-windows\AntiLagNext.exe`

---

## Smoke (critical paths)

| # | Step | Expected | P/F |
|---|------|----------|-----|
| 1 | Cold start | UI ≤ 5 s, no crash dialog, log «ready» | |
| 2 | Enable **Gaming** | Timer held, status Optimized, no error toast | |
| 3 | Disable / Reset all | Timer released, status Idle | |
| 4 | Enable **Max** | Applies; optional reboot dialog | |
| 5 | Enable **Office** | Lighter set than Gaming; success | |
| 6 | Health → Refresh | Audit + Drift tables fill | |
| 7 | Health → **Fix safe** | Success **or** detailed error (key + reason), not bare `Latency tweaks failed` | |
| 8 | Health → **Fix all** | ≥1 tweak written if findings CanFix; log shows detail | |
| 9 | Drift: change one catalog reg value → Refresh → **Reapply** | Status Drifted → then OK | |
| 10 | Chart on 2 min idle | Peak (1 min) does **not** only climb; can drop after quiet minute | |
| 11 | RU ↔ EN | Profile card + Health titles match language; EN has no Cyrillic labels | |
| 12 | Settings: tray hide → restore | Window returns | |
| 13 | Plugins: toggle one ext off → Enable | Apply still succeeds | |
| 14 | CLI elevated: `--status` | Exit 0, sensible text | |
| 15 | After Max: Reset all | Sampled registry keys restored / no stuck ActiveState | |

---

## Regression (Unreleased / known fixes)

| # | Case | Expected | P/F |
|---|------|----------|-----|
| R1 | Health fix on **missing** registry values (clean PC) | Snapshot does not throw; writes succeed | |
| R2 | Peak after spike then idle 90 s | Peak decays (rolling 60 s) | |
| R3 | NetworkThrottlingIndex | Single owner (catalog); no double plugin write by default | |
| R4 | Non-admin launch (optional) | Clear failure, no silent half-state | |

---

## i18n parity (automated)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\check-i18n.ps1
```

- [ ] Script exit code 0

---

## Notes / defects

| ID | Severity | Description | Build |
|----|----------|-------------|-------|
| | | | |

---

## Sign-off

- [ ] Smoke 1–15 PASS  
- [ ] Ready for release packaging  
- Tester: _____________  Date: _____________
