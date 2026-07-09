# Security Policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 1.x     | Yes       |

## Reporting a vulnerability

Please open a **private security advisory** on GitHub or email the maintainers (if listed in the repo). Do not file a public issue for exploitable privilege-escalation or data-loss bugs until a fix is available.

## What this app does (risk surface)

AntiLag Next **requires administrator rights** and may:

- Change the active Windows power plan and power settings
- Call `NtSetTimerResolution`
- Write HKCU/HKLM registry keys (Game Mode, HAGS, GPU driver keys)
- Create System Restore points (`SRSetRestorePoint`)
- Empty working sets of other processes (optional)

Treat any untrusted binary of this kind as high risk. Prefer building from source.

## Safe defaults

- Always use **Reset all** / backup restore before reporting regressions.
- Keep **Create restore point** enabled when System Restore is available.
- Disable game auto-switch if you do not want automatic profile application.
