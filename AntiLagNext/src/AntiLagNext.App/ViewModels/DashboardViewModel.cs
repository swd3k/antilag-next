using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>
/// Главная панель: переключатели оптимизаций, статус, применить / сбросить.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IProfileService _profiles;
    private readonly ISafetyService _safety;
    private readonly ITimerManager _timer;
    private readonly IPowerManager _power;
    private readonly ICoreParkingManager _parking;
    private readonly IGpuManager _gpu;
    private readonly ISettingsService _settings;
    private readonly IBenchmarkService _benchmark;

    [ObservableProperty] private bool _enableTimer = true;
    [ObservableProperty] private double _timerTargetMs = 0.5;
    [ObservableProperty] private bool _enablePowerScheme = true;
    [ObservableProperty] private bool _useUltimatePerformance;
    [ObservableProperty] private bool _enableCoreParking = true;
    [ObservableProperty] private CoreParkingMode _parkingMode = CoreParkingMode.AllActive;
    [ObservableProperty] private bool _enableGameMode = true;
    [ObservableProperty] private bool _enableHags = true;
    [ObservableProperty] private bool _enableMemoryCleanup;
    [ObservableProperty] private bool _enableGpuLowLatency = true;
    [ObservableProperty] private string _statusLine = "Оптимизации не активны";
    [ObservableProperty] private string _cpuTopology = "—";
    [ObservableProperty] private string _gpuVendor = "—";
    [ObservableProperty] private string _benchmarkSummary = string.Empty;
    [ObservableProperty] private bool _optimizationsActive;

    public Array ParkingModes => Enum.GetValues(typeof(CoreParkingMode));

    public DashboardViewModel(
        IProfileService profiles,
        ISafetyService safety,
        ITimerManager timer,
        IPowerManager power,
        ICoreParkingManager parking,
        IGpuManager gpu,
        ISettingsService settings,
        IBenchmarkService benchmark)
    {
        _profiles = profiles;
        _safety = safety;
        _timer = timer;
        _power = power;
        _parking = parking;
        _gpu = gpu;
        _settings = settings;
        _benchmark = benchmark;

        _timer.StateChanged += (_, s) =>
            App.Current.Dispatcher.Invoke(() => RefreshStatusFromSystem(s));

        LoadFromActiveProfile();
        RefreshSystemInfo();
    }

    public void LoadFromActiveProfile()
    {
        var p = _settings.Current.GetActiveProfile();
        EnableTimer = p.EnableTimer;
        TimerTargetMs = p.TimerTargetMs;
        EnablePowerScheme = p.EnablePowerScheme;
        UseUltimatePerformance = p.UseUltimatePerformance;
        EnableCoreParking = p.EnableCoreParkingControl;
        ParkingMode = p.CoreParkingMode;
        EnableGameMode = p.EnableGameModeTweak;
        EnableHags = p.EnableHags;
        EnableMemoryCleanup = p.EnableMemoryCleanup;
        EnableGpuLowLatency = p.EnableGpuLowLatency;
    }

    private void RefreshSystemInfo()
    {
        var topo = _parking.DetectTopology();
        CpuTopology = topo.Success && topo.Value != null ? topo.Value.ToString() : "не удалось определить";
        GpuVendor = _gpu.DetectVendor();
        RefreshStatusFromSystem(_timer.CurrentState);
    }

    private void RefreshStatusFromSystem(TimerState s)
    {
        var scheme = _power.GetActiveScheme();
        string schemeName = "неизвестно";
        if (scheme.Success)
        {
            var g = scheme.Value;
            if (g == PowerGuids.SchemeHighPerformance) schemeName = "High Performance";
            else if (g == PowerGuids.SchemeUltimatePerformance) schemeName = "Ultimate Performance";
            else if (g == PowerGuids.SchemeBalanced) schemeName = "Balanced";
            else if (g == PowerGuids.SchemePowerSaver) schemeName = "Power Saver";
            else schemeName = g.ToString()[..8] + "…";
        }

        OptimizationsActive = s.IsActive || schemeName is "High Performance" or "Ultimate Performance";
        StatusLine = s.IsActive
            ? $"Оптимизации активны: таймер {s.ActualMs:F3} мс, питание {schemeName}"
            : $"Состояние: таймер {(s.IsActive ? s.ActualMs.ToString("F3") + " мс" : "по умолчанию")}, питание {schemeName}";
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Применение…";
        try
        {
            var profile = BuildProfileFromToggles();
            var result = await _profiles.ApplyAsync(profile);
            StatusMessage = result.Message;
            OptimizationsActive = result.Success;
            RefreshStatusFromSystem(_timer.CurrentState);
            Log.Information("Apply: {Msg}", result.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка: " + ex.Message;
            Log.Error(ex, "Apply failed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Сброс…";
        try
        {
            var result = await _safety.ResetAllAsync();
            StatusMessage = result.Message;
            OptimizationsActive = false;
            RefreshStatusFromSystem(_timer.CurrentState);
            Log.Information("Reset: {Msg}", result.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка сброса: " + ex.Message;
            Log.Error(ex, "Reset failed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RunBenchmarkAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Бенчмарк…";
        try
        {
            var result = await _benchmark.RunAsync();
            if (result.Success && result.Value != null)
            {
                BenchmarkSummary = result.Value.Summary;
                StatusMessage = $"Рекомендация: {result.Value.RecommendedProfile}";
                // Применить рекомендованный профиль в toggles
                var preset = OptimizationProfile.CreatePreset(result.Value.RecommendedProfile);
                EnableTimer = preset.EnableTimer;
                TimerTargetMs = preset.TimerTargetMs;
                EnablePowerScheme = preset.EnablePowerScheme;
                EnableCoreParking = preset.EnableCoreParkingControl;
                ParkingMode = preset.CoreParkingMode;
                EnableGameMode = preset.EnableGameModeTweak;
                EnableHags = preset.EnableHags;
                EnableGpuLowLatency = preset.EnableGpuLowLatency;

                _settings.Current.FirstRunCompleted = true;
                _settings.Save();
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "Benchmark failed");
        }
        finally { IsBusy = false; }
    }

    private OptimizationProfile BuildProfileFromToggles() => new()
    {
        Name = "Текущие переключатели",
        Kind = ProfileKind.Custom,
        EnableTimer = EnableTimer,
        TimerTargetMs = TimerTargetMs,
        EnablePowerScheme = EnablePowerScheme,
        UseUltimatePerformance = UseUltimatePerformance,
        EnableCoreParkingControl = EnableCoreParking,
        CoreParkingMode = ParkingMode,
        EnableGameModeTweak = EnableGameMode,
        EnableHags = EnableHags,
        EnableMemoryCleanup = EnableMemoryCleanup,
        EnableGpuLowLatency = EnableGpuLowLatency,
        MaxPreRenderedFrames = EnableGpuLowLatency ? 1 : 0,
        MemoryCleanupExclusions = _settings.Current.GetActiveProfile().MemoryCleanupExclusions
    };
}
