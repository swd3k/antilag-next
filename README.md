# AntiLag Next

Modern open-source Windows latency / gaming performance tool — a clean-room successor to the ideas behind [AmbitiousPilots/AntiLag](https://github.com/AmbitiousPilots/AntiLag) (**not a fork**; original is closed binary, CC BY-NC-ND).

| | |
|---|---|
| Stack | **C# / .NET 8 / WPF** + optional C++ native DLL |
| License | **MIT** |
| OS | Windows 10 / 11 x64 (admin UAC) |

![AntiLag Next logo](logo.png)

## Features

- **Timer resolution** auto-tune (`NtSetTimerResolution` + QPC jitter probe)
- **Power plans** via Win32 `powrprof` (High / Ultimate Performance, processor min/max, ASPM)
- **Core parking** with hybrid P/E-core awareness
- **Game Mode / Game DVR / HAGS** registry tweaks
- **GPU low latency** (registry + native stub; NVAPI/ADLX optional later)
- **Memory working-set trim** with process exclusions
- **Safety first**: System Restore point + JSON backup of changed keys; one-click **Reset all**; backup history UI with point restore
- **Profiles**: Gaming / Office / Default + custom + game exe auto-switch (WMI)
- **Monitoring**: scheduling latency, timer res, system-wide CPU (`GetSystemTimes`), RAM
- **Tray**: minimize to tray to keep timer resolution held (important on Win11 22H2+)
- First-run **benchmark** with Russian recommendations

## Quick start

```powershell
# Requirements: .NET 8 SDK
cd AntiLagNext
dotnet restore
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release
dotnet run --project src\AntiLagNext.App -c Release
```

Publish a framework-dependent folder:

```powershell
.\scripts\publish.ps1
# → dist\AntiLagNext\
```

Portable data next to the exe: create empty file `AntiLagNext.portable`.

## Repository layout

```
logo.png                 # App logo (do not remove)
LICENSE                  # MIT
AntiLagNext/             # Solution + source
  src/AntiLagNext.App
  src/AntiLagNext.Core
  src/AntiLagNext.Infrastructure
  native/AntiLagNext.Native
  tests/
  scripts/               # publish + hard-test
```

Detailed architecture: [AntiLagNext/README.md](AntiLagNext/README.md).

## Disclaimer

Use at your own risk. Higher power consumption and temperatures are expected under High Performance + high timer resolution. HAGS changes may need a reboot. Always use **Reset all** if something feels wrong.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security reports: [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE). This project does **not** copy code from the original AntiLag binary.
