using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// Применение / откат профиля оптимизации.
/// Координирует SafetyService + все менеджеры оптимизаций.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly ISafetyService _safety;
    private readonly ITimerManager _timer;
    private readonly IPowerManager _power;
    private readonly ICoreParkingManager _parking;
    private readonly IGameModeManager _gameMode;
    private readonly IMemoryManager _memory;
    private readonly IGpuManager _gpu;
    private readonly IBackupService _backup;

    public ProfileService(
        ISafetyService safety,
        ITimerManager timer,
        IPowerManager power,
        ICoreParkingManager parking,
        IGameModeManager gameMode,
        IMemoryManager memory,
        IGpuManager gpu,
        IBackupService backup)
    {
        _safety = safety;
        _timer = timer;
        _power = power;
        _parking = parking;
        _gameMode = gameMode;
        _memory = memory;
        _gpu = gpu;
        _backup = backup;
    }

    public async Task<OperationResult> ApplyAsync(OptimizationProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile == null)
            return OperationResult.Fail("Профиль не задан.");

        var messages = new List<string>();
        var errors = new List<string>();

        // 1. Защита
        var before = await _safety.BeforeChangesAsync($"Активация профиля «{profile.Name}»", cancellationToken);
        // T? для unconstrained T=Guid не даёт Nullable<Guid> — Value это Guid
        if (!before.Success || before.Value == Guid.Empty)
            return OperationResult.Fail("Не удалось подготовить защиту.", detail: before.Detail ?? before.Message);

        Guid sessionId = before.Value;

        // Привязка сессии к менеджерам, умеющим snapshot
        if (_gameMode is GameModeManager gmm) gmm.BindBackupSession(sessionId);
        if (_gpu is GpuManager gpuMgr) gpuMgr.BindBackupSession(sessionId);

        try
        {
            // 2. Power scheme
            if (profile.EnablePowerScheme)
            {
                // Снимок текущей схемы и ключевых индексов ДО переключения
                var schemeBefore = _power.GetActiveScheme();
                if (schemeBefore.Success)
                {
                    SnapshotPower(sessionId, schemeBefore.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMinimumState, true);
                    SnapshotPower(sessionId, schemeBefore.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMinimumState, false);
                    SnapshotPower(sessionId, schemeBefore.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMaximumState, true);
                    SnapshotPower(sessionId, schemeBefore.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMaximumState, false);
                    SnapshotPower(sessionId, schemeBefore.Value, PowerGuids.SubPciExpress, PowerGuids.PciExpressAspm, true);
                    SnapshotPower(sessionId, schemeBefore.Value, PowerGuids.SubPciExpress, PowerGuids.PciExpressAspm, false);
                }

                Guid target = profile.UseUltimatePerformance
                    ? PowerGuids.SchemeUltimatePerformance
                    : PowerGuids.SchemeHighPerformance;

                // Ultimate может отсутствовать — fallback на High Performance
                var set = _power.SetActiveScheme(target);
                if (!set.Success && profile.UseUltimatePerformance)
                {
                    set = _power.SetActiveScheme(PowerGuids.SchemeHighPerformance);
                    messages.Add("Ultimate Performance недоступна — использована High Performance.");
                }
                if (set.Success) messages.Add(set.Message); else errors.Add(set.Message);

                // Min/Max processor state 100% + ASPM Off на целевой (активной) схеме
                var active = _power.GetActiveScheme();
                if (active.Success)
                {
                    SnapshotPower(sessionId, active.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMinimumState, true);
                    SnapshotPower(sessionId, active.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMinimumState, false);
                    SnapshotPower(sessionId, active.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMaximumState, true);
                    SnapshotPower(sessionId, active.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMaximumState, false);
                    _power.WriteValue(active.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMinimumState, 100, 100);
                    _power.WriteValue(active.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorMaximumState, 100, 100);

                    SnapshotPower(sessionId, active.Value, PowerGuids.SubPciExpress, PowerGuids.PciExpressAspm, true);
                    SnapshotPower(sessionId, active.Value, PowerGuids.SubPciExpress, PowerGuids.PciExpressAspm, false);
                    _power.WriteValue(active.Value, PowerGuids.SubPciExpress, PowerGuids.PciExpressAspm, 0, 0);
                }
            }

            // 3. Core parking
            if (profile.EnableCoreParkingControl)
            {
                var scheme = _power.GetActiveScheme();
                if (scheme.Success)
                {
                    SnapshotPower(sessionId, scheme.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorCoreParkingMinCores, true);
                    SnapshotPower(sessionId, scheme.Value, PowerGuids.SubProcessor, PowerGuids.ProcessorCoreParkingMinCores, false);
                    var park = _parking.ApplyMode(scheme.Value, profile.CoreParkingMode);
                    if (park.Success) messages.Add(park.Message); else errors.Add(park.Message);
                }
            }

            // 4. Timer
            if (profile.EnableTimer)
            {
                var tune = await _timer.TuneAsync(profile.TimerTargetMs, cancellationToken);
                if (tune.Success) messages.Add(tune.Message); else errors.Add(tune.Message);
            }

            // 5. Game Mode / HAGS
            if (profile.EnableGameModeTweak)
            {
                var gameModeResult = _gameMode.SetGameMode(enabled: true, disableGameDvr: true);
                if (gameModeResult.Success) messages.Add(gameModeResult.Message); else errors.Add(gameModeResult.Message);
            }
            if (profile.EnableHags)
            {
                var hags = _gameMode.SetHags(true);
                if (hags.Success) messages.Add(hags.Message); else errors.Add(hags.Message);
            }

            // 6. GPU
            if (profile.EnableGpuLowLatency)
            {
                var gpu = _gpu.SetLowLatencyMode(true);
                if (gpu.Success) messages.Add(gpu.Message); else errors.Add(gpu.Message);
                if (profile.MaxPreRenderedFrames > 0)
                {
                    var prf = _gpu.SetMaxPreRenderedFrames(profile.MaxPreRenderedFrames);
                    if (prf.Success) messages.Add(prf.Message); else errors.Add(prf.Message);
                }
            }

            // 7. Memory cleanup (одноразовая при активации)
            if (profile.EnableMemoryCleanup)
            {
                var mem = _memory.EmptyWorkingSets(profile.MemoryCleanupExclusions);
                if (mem.Success) messages.Add(mem.Message); else errors.Add(mem.Message);
            }

            // 8. Commit backup
            var commit = _safety.CommitChanges(sessionId);
            if (!commit.Success) errors.Add(commit.Message);

            if (errors.Count == 0)
                return OperationResult.Ok($"Профиль «{profile.Name}» применён. " + string.Join(" ", messages));

            return OperationResult.Fail(
                $"Профиль «{profile.Name}» применён частично.",
                detail: string.Join("; ", errors) + " | " + string.Join(" ", messages));
        }
        catch (Exception ex)
        {
            try { _safety.CommitChanges(sessionId); } catch { /* best-effort */ }
            return OperationResult.Fail("Сбой применения профиля.", detail: ex.Message, ex: ex);
        }
    }

    public async Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
        => await _safety.ResetAllAsync(cancellationToken);

    private void SnapshotPower(Guid sessionId, Guid scheme, Guid sub, Guid setting, bool isAc)
    {
        try
        {
            var read = _power.ReadValue(scheme, sub, setting, isAc);
            if (!read.Success) return;
            _backup.SnapshotPowerValue(sessionId, new PowerBackupEntry
            {
                SchemeGuid = scheme.ToString(),
                SubGroupGuid = sub.ToString(),
                SettingGuid = setting.ToString(),
                IsAc = isAc,
                OriginalValue = read.Value
            });
        }
        catch { /* best-effort snapshot */ }
    }
}
