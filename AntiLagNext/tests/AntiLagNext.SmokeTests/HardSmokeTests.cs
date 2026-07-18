using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Optimization;
using AntiLagNext.Infrastructure.Safety;
using AntiLagNext.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace AntiLagNext.SmokeTests;

/// <summary>
/// Жёсткий smoke: реальные Win32-вызовы через публичные менеджеры.
/// </summary>
public class HardSmokeTests : IDisposable
{
    private readonly List<Action> _cleanup = new();

    public void Dispose()
    {
        foreach (var a in _cleanup)
        {
            try { a(); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TimerManager_GetCaps_Succeeds()
    {
        var mgr = new TimerManager();
        var caps = mgr.GetCaps();
        caps.MinimumPeriod.Should().BeGreaterThan(0u);
        // Fine (min period) must be <= coarse (max period)
        caps.MaximumPeriod.Should().BeGreaterThanOrEqualTo(caps.MinimumPeriod);
        caps.MinimumMs.Should().BeInRange(0.1, 5.0);
        caps.MaximumMs.Should().BeInRange(caps.MinimumMs, 20.0);
    }

    [Fact]
    public async Task TimerManager_TuneAndRelease_Works()
    {
        var mgr = new TimerManager();
        var tune = await mgr.TuneAsync(1.0);
        tune.Success.Should().BeTrue(tune.Message + " | " + tune.Detail);
        tune.Value.Should().NotBeNull();
        tune.Value!.IsActive.Should().BeTrue();
        tune.Value.ActualMs.Should().BeInRange(0.4, 2.0);

        var rel = mgr.Release();
        rel.Success.Should().BeTrue(rel.Message);
        mgr.CurrentState.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PowerManager_GetActiveScheme_Works()
    {
        var power = new PowerManager();
        var scheme = power.GetActiveScheme();
        scheme.Success.Should().BeTrue(scheme.Message);
        scheme.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void PowerManager_ReadProcessorMinState_DoesNotCrash()
    {
        var power = new PowerManager();
        var scheme = power.GetActiveScheme();
        scheme.Success.Should().BeTrue();

        var read = power.ReadValue(
            scheme.Value,
            PowerGuids.SubProcessor,
            PowerGuids.ProcessorMinimumState,
            isAc: true);

        if (read.Success)
            read.Value.Should().BeInRange(0u, 100u);
    }

    [Fact]
    public void PowerManager_GetPowerSource_ReturnsAcOrDc()
    {
        var power = new PowerManager();
        var src = power.GetCurrentPowerSource();
        src.Should().BeOneOf(Core.Enums.PowerSource.Ac, Core.Enums.PowerSource.Dc);
    }

    [Fact]
    public void CoreParking_DetectTopology_Works()
    {
        var power = new PowerManager();
        var park = new CoreParkingManager(power);
        var topo = park.DetectTopology();
        topo.Success.Should().BeTrue(topo.Message);
        topo.Value.Should().NotBeNull();
        topo.Value!.LogicalProcessorCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Backup_RoundTrip_HkcuTempKey()
    {
        const string keyPath = @"Software\AntiLagNext\SmokeTest";
        const string valueName = "Probe";

        using (var key = Registry.CurrentUser.CreateSubKey(keyPath, true)!)
            key.SetValue(valueName, 42, RegistryValueKind.DWord);

        _cleanup.Add(() =>
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); } catch { }
        });

        var power = new PowerManager();
        var settings = Core.Settings.AppSettings.CreateDefault();
        var backup = new BackupService();
        var timer = new TimerManager();
        var gate = new AntiLagNext.Infrastructure.Services.SystemMutationGate();
        var plugins = new AntiLagNext.Infrastructure.Plugins.PluginCatalog(settings, backup);
        await plugins.LoadAsync();
        var safety = new SafetyService(backup, timer, power, plugins, settings, gate);

        var before = await safety.BeforeChangesAsync("SmokeTest", CancellationToken.None);
        before.Success.Should().BeTrue(before.Message);
        var session = before.Value;

        using (var key = Registry.CurrentUser.OpenSubKey(keyPath, false))
        {
            var existing = key!.GetValue(valueName);
            backup.SnapshotRegistryValue(session, new RegistryBackupEntry
            {
                Hive = "HKCU",
                KeyPath = keyPath,
                ValueName = valueName,
                ValueKind = (int)RegistryValueKind.DWord,
                SerializedValue = existing?.ToString(),
                WasMissing = existing == null
            });
        }

        using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true)!)
            key.SetValue(valueName, 99, RegistryValueKind.DWord);

        var commit = safety.CommitChanges(session);
        commit.Success.Should().BeTrue(commit.Message);

        var latest = backup.LoadLatest();
        latest.Success.Should().BeTrue(latest.Message);

        var restore = await backup.RestoreAsync(latest.Value!);
        restore.Success.Should().BeTrue(restore.Message + " " + restore.Detail);

        using (var key = Registry.CurrentUser.OpenSubKey(keyPath, false)!)
            Convert.ToInt32(key.GetValue(valueName)).Should().Be(42);
    }

    [Fact]
    public void GpuManager_DetectVendor_DoesNotThrow()
    {
        var gpu = new GpuManager(new BackupService());
        var vendor = gpu.DetectVendor();
        vendor.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MonitoringService_StartStop_EmitsSample()
    {
        var timer = new TimerManager();
        var power = new PowerManager();
        var mon = new MonitoringService(timer, power);

        MonitoringSample? last = null;
        mon.SampleArrived += (_, s) => last = s;

        mon.Start(TimeSpan.FromMilliseconds(150));
        try
        {
            // Wait for baseline + second sample (GetSystemTimes needs 2 ticks for CPU%)
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (last == null && DateTime.UtcNow < deadline)
                Thread.Sleep(50);

            last.Should().NotBeNull("monitoring should emit at least one sample");
            last!.TimerResolutionMs.Should().BeGreaterThan(0);
            last.UsedMemoryMb.Should().BeGreaterThan(0);
            last.CpuUsagePercent.Should().BeInRange(0, 100);
            last.ProbeCount.Should().BeGreaterThan(0);
            last.SchedulingLatencyMaxUs.Should().BeGreaterThanOrEqualTo(last.SchedulingLatencyUs - 0.01);
        }
        finally
        {
            mon.Stop();
            mon.Dispose();
        }
    }

    [Fact]
    public void BackupService_LoadAll_DoesNotThrow()
    {
        var backup = new BackupService();
        var all = backup.LoadAll();
        all.Should().NotBeNull();
    }

    [Fact]
    public async Task ProfileService_ApplyDefault_ThenReset_DoesNotThrow()
    {
        // Soft profile: only timer + no aggressive registry if default
        var power = new PowerManager();
        var settings = Core.Settings.AppSettings.CreateDefault();
        var backup = new BackupService();
        var timer = new TimerManager();
        var gate = new AntiLagNext.Infrastructure.Services.SystemMutationGate();
        var plugins = new AntiLagNext.Infrastructure.Plugins.PluginCatalog(settings, backup);
        await plugins.LoadAsync();
        // Disable extension plugins for smoke (avoid HKLM MMCSS without cleanup)
        foreach (var p in plugins.Plugins.Where(x => !x.AppliedByCore))
            p.IsEnabled = false;

        var safety = new SafetyService(backup, timer, power, plugins, settings, gate);
        var parking = new CoreParkingManager(power);
        var gameMode = new GameModeManager(backup);
        var memory = new MemoryManager();
        var gpu = new GpuManager(backup);
        var desired = new AntiLagNext.Infrastructure.Storage.DesiredStateStore();
        var tweakEngine = new AntiLagNext.Infrastructure.Tweaks.RegistryTweakEngine(backup, desired);
        var profiles = new ProfileService(safety, timer, power, parking, gameMode, memory, gpu, backup, plugins, gate, tweakEngine);

        // Default kind → empty catalog ForProfile; only timer (no HKLM tweaks without admin)
        var soft = OptimizationProfile.CreatePreset(Core.Enums.ProfileKind.Default);
        soft.EnablePowerScheme = false;
        soft.EnableCoreParkingControl = false;
        soft.EnableGameModeTweak = false;
        soft.EnableHags = false;
        soft.EnableGpuLowLatency = false;
        soft.EnableMemoryCleanup = false;
        soft.EnableTimer = true;
        soft.TimerTargetMs = 1.0;

        var apply = await profiles.ApplyAsync(soft);
        // May partially fail without admin — must not throw
        apply.Should().NotBeNull();
        apply.Message.Should().NotBeNullOrWhiteSpace();
        apply.Success.Should().BeTrue(apply.Message + " | " + apply.Detail);

        var reset = await profiles.RevertAsync();
        reset.Should().NotBeNull();
        timer.CurrentState.IsActive.Should().BeFalse("Reset must release timer");
        AntiLagNext.Infrastructure.Storage.ActiveStateStore.IsActive().Should().BeFalse();
    }

    [Fact]
    public async Task ResetAll_WithoutPriorApply_DoesNotThrow()
    {
        var power = new PowerManager();
        var settings = Core.Settings.AppSettings.CreateDefault();
        var backup = new BackupService();
        var timer = new TimerManager();
        var gate = new AntiLagNext.Infrastructure.Services.SystemMutationGate();
        var plugins = new AntiLagNext.Infrastructure.Plugins.PluginCatalog(settings, backup);
        await plugins.LoadAsync();
        foreach (var p in plugins.Plugins.Where(x => !x.AppliedByCore))
            p.IsEnabled = false;
        var safety = new SafetyService(backup, timer, power, plugins, settings, gate);

        var reset = await safety.ResetAllAsync();
        reset.Should().NotBeNull();
        timer.CurrentState.IsActive.Should().BeFalse();
    }
}
