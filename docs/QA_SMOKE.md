# AntiLag Next — Smoke / Regression checklist

**Product:** AntiLag Next (Windows, elevated)  
**Target:** ≤ 20 minutes per build  
**Machines:** Win11 x64 (primary), Win10 22H2 optional; **one laptop on battery** for R8  
**Build:** `dotnet build AntiLagNext/AntiLagNext.sln -c Release`  
**Tests:** `dotnet test AntiLagNext/AntiLagNext.sln -c Release` must be green first  
**Current checklist:** 1.4.1

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
- [ ] `scripts\check-i18n.ps1` → exit 0
- [ ] Launch **as Administrator** from  
  `AntiLagNext\src\AntiLagNext.Ui\bin\Release\net8.0-windows\AntiLagNext.exe`
- [ ] Sidebar version reads **1.4.1** immediately (not a 1.3.1 flash)

---

## Smoke (critical paths)

| # | Step | Expected | P/F |
|---|------|----------|-----|
| 1 | Cold start | UI ≤ 5 s, no crash dialog, log «ready», chip logo in title bar / tray | |
| 2 | Enable **Gaming** | Timer held, status Optimized, no error toast; HAGS **not** turned on | |
| 3 | Disable / Reset all | Timer released, status Idle | |
| 4 | Enable **Max** | Applies; optional reboot dialog; HAGS still off unless opted in | |
| 5 | Enable **Office** | Lighter set than Gaming; success | |
| 6 | Health → Refresh | Audit + Drift tables fill | |
| 7 | Health → **Fix recommended** / **Fix safe** | Success **or** detailed error (key + reason), not bare `Latency tweaks failed` | |
| 8 | Health → **Fix all** | ≥1 tweak written if findings CanFix; log shows detail | |
| 9 | Drift: change one catalog reg value → Refresh → **Reapply** | Status Drifted → then OK | |
| 10 | Chart on 2 min idle | Peak (1 min) does **not** only climb; can drop after quiet minute | |
| 11 | RU ↔ EN | Profile card + Health titles match language; EN has no Cyrillic labels | |
| 12 | Settings: tray hide → restore | Window returns | |
| 13 | Plugins: `ext.process.priority` is **off**; toggle one ext off → Enable | Apply still succeeds; AntiLag process stays Normal | |
| 14 | CLI elevated: `--status` | Exit 0, sensible text | |
| 15 | After Max: Reset all | Sampled registry keys restored / no stuck ActiveState | |

---

## Regression (1.4.0 / 1.4.1)

| # | Case | Expected | P/F |
|---|------|----------|-----|
| R1 | Health fix on **missing** registry values (clean PC) | Snapshot does not throw; writes succeed | |
| R2 | Peak after spike then idle 90 s | Peak decays (rolling 60 s) | |
| R3 | NetworkThrottlingIndex | Single owner (catalog); no double plugin write by default | |
| R4 | Non-admin launch (optional) | Clear failure, no silent half-state | |
| R5 | Win11 22H2+: Enable → timer scope | UI shows *global* **or** *reboot needed* (not silent process-only forever) | |
| R6 | Built-in Gaming/Max | HAGS off; working-set trim off | |
| R7 | Process-priority plugin on + game listed | Game exe High; **AntiLag.exe stays Normal** | |
| R8 | Laptop **on battery**: Enable | No DC min-CPU / ASPM writes; scheme not switched; cores not pinned 100% | |
| R9 | Crash mid-Enable then relaunch | Restores **that session** JSON only (a newer planted backup is ignored) | |
| R10 | In-app update (if a newer tag exists) | Refuses Setup without matching `SHA256SUMS.txt`; User-Agent is 1.4.1 | |
| R11 | Autostart confirm | Task `/TR` is the real exe; extra `& \| < > ^ %` rejected | |

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
- [ ] 1.4.0 / 1.4.1 regressions R5–R11 PASS (R8 on a laptop; R10 when an update is available)  
- [ ] Ready for release packaging  
- Tester: _____________  Date: _____________
