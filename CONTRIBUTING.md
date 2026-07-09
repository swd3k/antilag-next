# Contributing to AntiLag Next

Thanks for helping improve an open-source Windows latency/performance tool.

## Ground rules

- **No closed-source AntiLag code.** Original [AmbitiousPilots/AntiLag](https://github.com/AmbitiousPilots/AntiLag) is CC BY-NC-ND; this project is a clean-room reimplementation under MIT.
- Prefer **Win32 APIs** (`powrprof`, `ntdll`, `kernel32`) over shelling out to `powercfg.exe` when possible.
- Every system mutation must go through **backup/restore** (`ISafetyService` / `IBackupService`).
- UI stays **WPF + MVVM** (`CommunityToolkit.Mvvm`). Domain code lives in `AntiLagNext.Core`.

## Setup

Requirements: Windows 10/11 x64, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release
```

Run the app (elevated UAC):

```powershell
dotnet run --project src\AntiLagNext.App -c Release
```

## Hard smoke tests

```powershell
.\scripts\hard-test.ps1
```

Smoke tests call real Win32 APIs (timer resolution, power scheme read, registry backup round-trip). Run as a normal user; some power writes may need admin.

## Pull requests

1. Keep PRs focused (one feature/fix).
2. Add/adjust unit tests for Core models; add smoke tests for Win32-facing paths when practical.
3. Update `AntiLagNext/README.md` if user-facing behavior changes.
4. Do not commit `bin/`, `obj/`, logs, or personal `%APPDATA%` backups.

## Code style

- C# 12, nullable enabled.
- Russian user-facing strings are OK (current UI language).
- English comments OK for complex Win32 paths.
