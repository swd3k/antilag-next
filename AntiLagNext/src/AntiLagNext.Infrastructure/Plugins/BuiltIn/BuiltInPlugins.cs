using System.Collections.Concurrent;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Infrastructure.Safety;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Plugins.BuiltIn;

/// <summary>Базовый класс built-in плагинов с простыми key/value settings.</summary>
public abstract class BuiltInPluginBase : IAntiLagPlugin
{
    private readonly ConcurrentDictionary<string, object?> _settings = new(StringComparer.OrdinalIgnoreCase);
    protected IPluginServices? Services { get; private set; }
    protected PluginStatus LastStatus { get; set; } = new() { State = PluginRuntimeState.Idle, Message = "Idle" };

    public abstract string Id { get; }
    public abstract string NameKey { get; }
    public abstract string DescriptionKey { get; }
    public virtual string Version => "1.0.0";
    public abstract PluginCategory Category { get; }
    public abstract LatencyImpact Impact { get; }
    public bool IsBuiltIn => true;
    public virtual int ApplyOrder => 100;
    public bool IsEnabled { get; set; } = true;
    public abstract bool AppliedByCore { get; }

    public virtual bool IsSupported(out string? reason)
    {
        reason = null;
        return true;
    }

    public virtual PluginStatus GetStatus() => LastStatus;

    public virtual Task InitializeAsync(IPluginServices services, CancellationToken cancellationToken = default)
    {
        Services = services;
        foreach (var d in GetUiDescriptors())
            _settings.TryAdd(d.Key, d.DefaultValue);
        return Task.CompletedTask;
    }

    public abstract Task<OperationResult> ApplyAsync(PluginApplyContext context);

    public virtual Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok($"{Id}: revert n/a"));

    public virtual IReadOnlyList<PluginUiDescriptor> GetUiDescriptors() => Array.Empty<PluginUiDescriptor>();

    public IReadOnlyDictionary<string, object?> GetSettingValues() =>
        new Dictionary<string, object?>(_settings, StringComparer.OrdinalIgnoreCase);

    public void SetSettingValue(string key, object? value) => _settings[key] = value;

    protected T GetSetting<T>(string key, T fallback)
    {
        if (!_settings.TryGetValue(key, out var v) || v is null) return fallback;
        try
        {
            if (v is T t) return t;
            return (T)Convert.ChangeType(v, typeof(T));
        }
        catch { return fallback; }
    }

    protected void SetStatus(PluginRuntimeState state, string message)
    {
        LastStatus = new PluginStatus
        {
            State = state,
            Message = message,
            LastChangedUtc = DateTime.UtcNow
        };
    }

    public virtual void Dispose() { }
}

/// <summary>Документирует timer path (apply = ProfileService).</summary>
public sealed class TimerCorePlugin : BuiltInPluginBase
{
    public override string Id => "core.timer";
    public override string NameKey => "plugin.timer.name";
    public override string DescriptionKey => "plugin.timer.desc";
    public override PluginCategory Category => PluginCategory.Core;
    public override LatencyImpact Impact => LatencyImpact.High;
    public override bool AppliedByCore => true;

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context) =>
        Task.FromResult(OperationResult.Ok("core.timer: applied by ProfileService"));
}

public sealed class PowerCorePlugin : BuiltInPluginBase
{
    public override string Id => "core.power";
    public override string NameKey => "plugin.power.name";
    public override string DescriptionKey => "plugin.power.desc";
    public override PluginCategory Category => PluginCategory.Power;
    public override LatencyImpact Impact => LatencyImpact.High;
    public override bool AppliedByCore => true;

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context) =>
        Task.FromResult(OperationResult.Ok("core.power: applied by ProfileService"));
}

public sealed class GpuCorePlugin : BuiltInPluginBase
{
    public override string Id => "core.gpu";
    public override string NameKey => "plugin.gpu.name";
    public override string DescriptionKey => "plugin.gpu.desc";
    public override PluginCategory Category => PluginCategory.Gpu;
    public override LatencyImpact Impact => LatencyImpact.Medium;
    public override bool AppliedByCore => true;

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context) =>
        Task.FromResult(OperationResult.Ok("core.gpu: applied by ProfileService"));
}

/// <summary>
/// Best-effort network latency hygiene: Multimedia Class Scheduler + DSCP defaults.
/// Не CoDel/PIE и не userspace UDP — честный medium/experimental impact.
/// Snapshots registry into the active backup session (same pattern as GameModeManager/GpuManager)
/// when <see cref="PluginApplyContext.BackupSessionId"/> is set and IBackupService is available.
/// </summary>
public sealed class NetworkQosPlugin : BuiltInPluginBase
{
    private const string MmcssKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTaskKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    private readonly IBackupService? _backup;
    private int _prevResponsiveness = 20;

    public NetworkQosPlugin(IBackupService? backup = null) => _backup = backup;

    public override string Id => "ext.network.qos";
    public override string NameKey => "plugin.net.name";
    public override string DescriptionKey => "plugin.net.desc";
    public override PluginCategory Category => PluginCategory.Network;
    public override LatencyImpact Impact => LatencyImpact.Low;
    public override bool AppliedByCore => false;

    public override IReadOnlyList<PluginUiDescriptor> GetUiDescriptors() => new[]
    {
        new PluginUiDescriptor
        {
            Key = "enableMmcss",
            LabelKey = "plugin.net.mmcss",
            TooltipKey = "plugin.net.mmcss.tip",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = true
        }
    };

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        try
        {
            // SystemResponsiveness: lower = more CPU for multimedia/games (0–100, default 20)
            using var key = Registry.LocalMachine.CreateSubKey(MmcssKey, true);
            if (key == null)
                return Task.FromResult(OperationResult.Fail("MMCSS key unavailable"));

            if (GetSetting("enableMmcss", true))
            {
                // Best-effort session snapshots so Reset/Restore can undo MMCSS + Games task keys
                SnapshotIfSession(context.BackupSessionId, MmcssKey, "SystemResponsiveness");
                SnapshotIfSession(context.BackupSessionId, GamesTaskKey, "GPU Priority");
                SnapshotIfSession(context.BackupSessionId, GamesTaskKey, "Priority");
                SnapshotIfSession(context.BackupSessionId, GamesTaskKey, "Scheduling Category");
                SnapshotIfSession(context.BackupSessionId, GamesTaskKey, "SFIO Priority");

                _prevResponsiveness = key.GetValue("SystemResponsiveness") as int? ?? 20;
                key.SetValue("SystemResponsiveness", 10, RegistryValueKind.DWord);
                // Games task priority boost (documented gaming tweak)
                using var games = key.CreateSubKey(@"Tasks\Games", true);
                games?.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                games?.SetValue("Priority", 6, RegistryValueKind.DWord);
                games?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                games?.SetValue("SFIO Priority", "High", RegistryValueKind.String);
            }

            Services?.LogInfo("Network/MMCSS profile applied (best-effort, reboot may help)");
            return Task.FromResult(OperationResult.Ok("Network: MMCSS/Games task profile applied"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("Network plugin failed", detail: ex.Message));
        }
    }

    public override Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(MmcssKey, true);
            key?.SetValue("SystemResponsiveness", _prevResponsiveness, RegistryValueKind.DWord);
            return Task.FromResult(OperationResult.Ok($"Network: SystemResponsiveness restored to {_prevResponsiveness}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Snapshot current HKLM value into backup session before mutate (GameModeManager-style).
    /// No-op if backup service missing or session empty — in-memory _prevResponsiveness remains fallback.
    /// </summary>
    private void SnapshotIfSession(Guid sessionId, string keyPath, string valueName)
    {
        if (_backup == null || sessionId == Guid.Empty)
            return;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            var existing = key?.GetValue(valueName);
            var kind = existing == null
                ? RegistryValueKind.DWord
                : key!.GetValueKind(valueName);

            string? serialized = existing switch
            {
                null => null,
                int i => i.ToString(),
                string s => s,
                _ => existing.ToString()
            };

            _backup.SnapshotRegistryValue(sessionId, new RegistryBackupEntry
            {
                Hive = "HKLM",
                KeyPath = keyPath,
                ValueName = valueName,
                ValueKind = (int)kind,
                SerializedValue = serialized,
                WasMissing = existing == null
            });
        }
        catch
        {
            /* best-effort snapshot — apply still proceeds */
        }
    }
}

/// <summary>Поднимает приоритет процесса AntiLag (и опционально игр из профиля) при apply.</summary>
public sealed class ProcessPriorityPlugin : BuiltInPluginBase
{
    public override string Id => "ext.process.priority";
    public override string NameKey => "plugin.prio.name";
    public override string DescriptionKey => "plugin.prio.desc";
    public override PluginCategory Category => PluginCategory.Game;
    public override LatencyImpact Impact => LatencyImpact.Low;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 80;

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            p.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;

            int gamesBoosted = 0;
            foreach (var exe in context.Profile.GameExecutables)
            {
                string name = Path.GetFileNameWithoutExtension(exe);
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var gp in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    try
                    {
                        gp.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
                        gamesBoosted++;
                    }
                    catch { /* access denied */ }
                    finally { gp.Dispose(); }
                }
            }

            string msg = gamesBoosted > 0
                ? $"Process: High priority (self + {gamesBoosted} game proc)"
                : "Process: High priority class set (self)";
            SetStatus(PluginRuntimeState.Applied, msg);
            return Task.FromResult(OperationResult.Ok(msg));
        }
        catch (Exception ex)
        {
            SetStatus(PluginRuntimeState.Error, ex.Message);
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    public override Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            p.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
            SetStatus(PluginRuntimeState.Idle, "Normal priority");
            return Task.FromResult(OperationResult.Ok("Process: Normal priority restored"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }
}

/// <summary>DNS cache flush + light network hygiene (no permanent netsh reset).</summary>
public sealed class NetworkHygienePlugin : BuiltInPluginBase
{
    public override string Id => "ext.network.hygiene";
    public override string NameKey => "plugin.dns.name";
    public override string DescriptionKey => "plugin.dns.desc";
    public override PluginCategory Category => PluginCategory.Network;
    public override LatencyImpact Impact => LatencyImpact.Low;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 90;

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        try
        {
            // Prefer absolute System32 path — avoid PATH hijack under elevation
            string ipconfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "ipconfig.exe");
            if (!File.Exists(ipconfig))
                ipconfig = "ipconfig.exe";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ipconfig,
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Do not inherit elevated token environment PATH tricks
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null && !proc.WaitForExit(5000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return Task.FromResult(OperationResult.Fail("DNS flush timed out"));
            }
            SetStatus(PluginRuntimeState.Applied, "DNS cache flushed");
            Services?.LogInfo("DNS cache flushed");
            return Task.FromResult(OperationResult.Ok("Network: DNS cache flushed"));
        }
        catch (Exception ex)
        {
            SetStatus(PluginRuntimeState.Error, ex.Message);
            return Task.FromResult(OperationResult.Fail("DNS flush failed", detail: ex.Message));
        }
    }
}

/// <summary>Optional safe service start-type tweaks (allowlist only).</summary>
public sealed class ServiceOptimizerPlugin : BuiltInPluginBase
{
    private readonly IBackupService? _backup;

    public ServiceOptimizerPlugin(IBackupService? backup = null) => _backup = backup;

    public override string Id => "ext.services.safe";
    public override string NameKey => "plugin.svc.name";
    public override string DescriptionKey => "plugin.svc.desc";
    public override PluginCategory Category => PluginCategory.Experimental;
    public override LatencyImpact Impact => LatencyImpact.Medium;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 120;

    public override IReadOnlyList<PluginUiDescriptor> GetUiDescriptors() => new[]
    {
        new PluginUiDescriptor
        {
            Key = "disableSysMain",
            LabelKey = "plugin.svc.sysmain",
            TooltipKey = "plugin.svc.sysmain.tip",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false
        },
        new PluginUiDescriptor
        {
            Key = "disableDiagTrack",
            LabelKey = "plugin.svc.diag",
            TooltipKey = "plugin.svc.diag.tip",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false
        }
    };

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        var changed = new List<string>();
        try
        {
            if (GetSetting("disableSysMain", false))
                TrySetManual(context.BackupSessionId, "SysMain", changed);
            if (GetSetting("disableDiagTrack", false))
                TrySetManual(context.BackupSessionId, "DiagTrack", changed);

            if (changed.Count == 0)
            {
                SetStatus(PluginRuntimeState.Idle, "No service tweaks enabled");
                return Task.FromResult(OperationResult.Ok("Services: nothing enabled (opt-in)"));
            }

            string msg = "Services → Manual: " + string.Join(", ", changed);
            SetStatus(PluginRuntimeState.Applied, msg);
            return Task.FromResult(OperationResult.Ok(msg));
        }
        catch (Exception ex)
        {
            SetStatus(PluginRuntimeState.Error, ex.Message);
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    private void TrySetManual(Guid sessionId, string serviceName, List<string> changed)
    {
        if (!ServiceAllowList.IsSafe(serviceName)) return;

        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
        if (key == null) return;

        int original = key.GetValue("Start") as int? ?? 3;
        if (_backup != null && sessionId != Guid.Empty)
        {
            _backup.SnapshotService(sessionId, new ServiceBackupEntry
            {
                ServiceName = serviceName,
                OriginalStartType = original,
                WasRunning = false
            });
        }

        // 3 = SERVICE_DEMAND_START (Manual)
        key.SetValue("Start", 3, RegistryValueKind.DWord);
        changed.Add(serviceName);
    }
}

/// <summary>Registry pack: Network Throttling Index + GameDVR policies (HPET left optional).</summary>
public sealed class RegistryTweaksPlugin : BuiltInPluginBase
{
    private readonly IBackupService? _backup;
    private const string MultimediaKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GameDvrPolicy =
        @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";

    public RegistryTweaksPlugin(IBackupService? backup = null) => _backup = backup;

    public override string Id => "ext.registry.tweaks";
    public override string NameKey => "plugin.reg.name";
    public override string DescriptionKey => "plugin.reg.desc";
    public override PluginCategory Category => PluginCategory.Network;
    public override LatencyImpact Impact => LatencyImpact.Medium;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 70;

    public override IReadOnlyList<PluginUiDescriptor> GetUiDescriptors() => new[]
    {
        // NetworkThrottling is owned by TweakCatalog (network.throttling_index) — do not dual-write.
        new PluginUiDescriptor
        {
            Key = "networkThrottling",
            LabelKey = "plugin.reg.throttle",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false // catalog applies this for Gaming/Max/Office
        },
        new PluginUiDescriptor
        {
            Key = "disableGameDvrPolicy",
            LabelKey = "plugin.reg.gamedvr",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = true
        }
    };

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        try
        {
            var parts = new List<string>();
            // Optional legacy path only if user re-enables the toggle (catalog is primary).
            if (GetSetting("networkThrottling", false))
            {
                Snapshot(context.BackupSessionId, MultimediaKey, "NetworkThrottlingIndex");
                using var key = Registry.LocalMachine.CreateSubKey(MultimediaKey, true);
                key?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFFu), RegistryValueKind.DWord);
                parts.Add("NetworkThrottlingIndex=max (plugin)");
            }

            if (GetSetting("disableGameDvrPolicy", true))
            {
                Snapshot(context.BackupSessionId, GameDvrPolicy, "AllowGameDVR");
                using var key = Registry.LocalMachine.CreateSubKey(GameDvrPolicy, true);
                key?.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                parts.Add("GameDVR policy off");
            }

            string msg = parts.Count == 0
                ? "Registry plugin: skipped (catalog owns throttling)"
                : "Registry: " + string.Join(", ", parts);
            SetStatus(PluginRuntimeState.Applied, msg);
            return Task.FromResult(OperationResult.Ok(msg));
        }
        catch (Exception ex)
        {
            SetStatus(PluginRuntimeState.Error, ex.Message);
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    private void Snapshot(Guid sessionId, string keyPath, string valueName)
    {
        if (_backup == null || sessionId == Guid.Empty) return;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);
            var existing = key?.GetValue(valueName);
            var kind = existing == null ? RegistryValueKind.DWord : key!.GetValueKind(valueName);
            _backup.SnapshotRegistryValue(sessionId, new RegistryBackupEntry
            {
                Hive = "HKLM",
                KeyPath = keyPath,
                ValueName = valueName,
                ValueKind = (int)kind,
                SerializedValue = existing?.ToString(),
                WasMissing = existing == null
            });
        }
        catch { /* best-effort */ }
    }
}

/// <summary>Experimental MSI mode — MVP: status only / not auto-applied.</summary>
public sealed class ExperimentalMsiPlugin : BuiltInPluginBase
{
    public override string Id => "exp.msi";
    public override string NameKey => "plugin.msi.name";
    public override string DescriptionKey => "plugin.msi.desc";
    public override PluginCategory Category => PluginCategory.Experimental;
    public override LatencyImpact Impact => LatencyImpact.Experimental;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 900;

    public override bool IsSupported(out string? reason)
    {
        reason = "MSI Mode: experimental — enable only after backup; full device enum in next release.";
        return true; // discoverable but off by default
    }

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        SetStatus(PluginRuntimeState.Partial, "MSI Mode: no-op in MVP (experimental stub)");
        return Task.FromResult(OperationResult.Ok(
            "MSI Mode: stub — device enumeration planned; no changes applied."));
    }
}

/// <summary>Experimental interrupt affinity — stub (TZ: careful, undocumented).</summary>
public sealed class ExperimentalInterruptAffinityPlugin : BuiltInPluginBase
{
    public override string Id => "exp.irq.affinity";
    public override string NameKey => "plugin.irq.name";
    public override string DescriptionKey => "plugin.irq.desc";
    public override PluginCategory Category => PluginCategory.Experimental;
    public override LatencyImpact Impact => LatencyImpact.Experimental;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 910;

    public override bool IsSupported(out string? reason)
    {
        reason = "Interrupt Affinity requires careful hardware testing; disabled auto-apply.";
        return false;
    }

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        SetStatus(PluginRuntimeState.Unsupported, "Not auto-applied");
        return Task.FromResult(OperationResult.Ok(
            "Interrupt Affinity: not applied (unsupported / experimental)."));
    }
}

/// <summary>Experimental driver blacklist — allowlist edit only, no boot drivers.</summary>
public sealed class ExperimentalDriverBlacklistPlugin : BuiltInPluginBase
{
    public override string Id => "exp.driver.blacklist";
    public override string NameKey => "plugin.drv.name";
    public override string DescriptionKey => "plugin.drv.desc";
    public override PluginCategory Category => PluginCategory.Experimental;
    public override LatencyImpact Impact => LatencyImpact.Experimental;
    public override bool AppliedByCore => false;
    public override int ApplyOrder => 920;

    public override IReadOnlyList<PluginUiDescriptor> GetUiDescriptors() => new[]
    {
        new PluginUiDescriptor
        {
            Key = "drivers",
            LabelKey = "plugin.drv.list",
            Kind = PluginSettingKind.Text,
            DefaultValue = ""
        }
    };

    public override Task<OperationResult> ApplyAsync(PluginApplyContext context)
    {
        // Refuse empty or dangerous — MVP never mutates without explicit non-empty safe name via ServiceAllowList
        string list = GetSetting("drivers", "") ?? "";
        if (string.IsNullOrWhiteSpace(list))
        {
            SetStatus(PluginRuntimeState.Idle, "Empty blacklist");
            return Task.FromResult(OperationResult.Ok("Driver blacklist: empty — no changes"));
        }

        SetStatus(PluginRuntimeState.Partial, "Blacklist recorded but not applied in MVP");
        return Task.FromResult(OperationResult.Ok(
            "Driver blacklist: MVP records settings only; service mutations go through ServiceOptimizer allowlist."));
    }
}
