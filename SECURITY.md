# Security Policy

**Maintainer:** [swd3k](https://github.com/swd3k)  
**Repository:** [github.com/swd3k/antilag-next](https://github.com/swd3k/antilag-next)

## Supported versions

| Version | Supported |
| ------- | --------- |
| 1.x     | Yes       |

## Reporting a vulnerability

Please open a **private security advisory** on GitHub (preferred) or contact **swd3k** via GitHub.  
Do not file a public issue for exploitable privilege-escalation or data-loss bugs until a fix is available.

## What this app does (risk surface)

AntiLag Next **requires administrator rights** and may:

- Change the active Windows power plan and power settings
- Call `NtSetTimerResolution`
- Write HKCU/HKLM registry keys (Game Mode, HAGS, GPU driver keys)
- Create System Restore points (`SRSetRestorePoint`)
- Empty working sets of other processes (optional)
- Register a logon Task Scheduler job (only after user confirmation)

Treat any untrusted binary of this kind as high risk. Prefer building from source.

## Safe defaults

- Always use **Reset all** / backup restore before reporting regressions.
- Keep **Create restore point** enabled when System Restore is available.
- Experimental plugins are stubs / disabled in the shipping UI.
- External `*.plugin.dll` loading is opt-in (`AllowExternalPlugins`).
