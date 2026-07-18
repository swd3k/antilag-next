# Contributing to AntiLag Next

Thanks for helping improve this open-source Windows latency / performance tool.

**Author / maintainer:** [swd3k](https://github.com/swd3k)

## Ground rules

- **No closed-source AntiLag code.** Original [AmbitiousPilots/AntiLag](https://github.com/AmbitiousPilots/AntiLag) is CC BY-NC-ND; this project is a clean-room reimplementation under **MIT**.
- Prefer **Win32 APIs** (`powrprof`, `ntdll`, `kernel32`) over shelling out when possible.
- Every system mutation must go through **backup/restore** (`ISafetyService` / `IBackupService`).
- **Shipping UI** is **Photino + WebView2** (`AntiLagNext.Ui` + `wwwroot`). Domain code lives in `AntiLagNext.Core`.
- Shipping GUI is Photino (`AntiLagNext.Ui`) only (legacy WPF removed in 1.2.0).

## Setup

Requirements: Windows 10/11 x64, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release
```

Run the shipping UI (elevated UAC):

```powershell
dotnet run --project src\AntiLagNext.Ui -c Release
```

CLI:

```powershell
dotnet run --project src\AntiLagNext.Cli -c Release -- --status
```

## Hard smoke tests

```powershell
.\scripts\hard-test.ps1
```

Smoke tests call real Win32 APIs (timer resolution, power scheme read, registry backup round-trip). Run as a normal user; some power writes may need admin.

## Pull requests

1. Keep PRs focused (one feature/fix).
2. Add/adjust unit tests for Core models; add smoke tests for Win32-facing paths when practical.
3. Update root [README.md](README.md) if user-facing behavior changes.
4. Do not commit `bin/`, `obj/`, `dist/`, `backups/`, logs, or personal `%APPDATA%` data.

## Code style

- C# 12, nullable enabled.
- User-facing strings: Russian + English (`wwwroot/i18n`).
- English comments OK for complex Win32 paths.
