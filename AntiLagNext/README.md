# AntiLag Next (solution)

**Author:** [swd3k](https://github.com/swd3k) · **Repo:** [github.com/swd3k/antilag-next](https://github.com/swd3k/antilag-next)

Full user documentation: **[../README.md](../README.md)**

## Projects

| Project | Role |
|---------|------|
| `AntiLagNext.Core` | Models, contracts, plugin interface |
| `AntiLagNext.Infrastructure` | Win32 managers, safety, plugins host |
| **`AntiLagNext.Ui`** | **Shipping** Photino + WebView2 UI |
| `AntiLagNext.Cli` | Console: `--apply` / `--revert` / `--status` |
| `tests/*` | Unit + Win32 smoke |

## Build

```powershell
dotnet restore AntiLagNext.sln
dotnet build AntiLagNext.sln -c Release
dotnet test AntiLagNext.sln -c Release
```

Publish portable (from repo root):

```powershell
..\scripts\publish.ps1
```

## License

MIT © 2026 swd3k
