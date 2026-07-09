using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>Главная панель — layout mockup SYSTEM STATUS + реальные метрики.</summary>
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
    private readonly IMonitoringService _monitoring;
    private readonly MonitoringViewModel _monitoringVm;

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
    [ObservableProperty] private string _activeProfileName = "—";
    [ObservableProperty] private string _powerSchemeName = "—";
    [ObservableProperty] private double _liveLatencyUs;
    [ObservableProperty] private double _liveTimerMs;
    [ObservableProperty] private string _primaryCtaLabel = "ENABLE OPTIMIZATION";
    [ObservableProperty] private string _latencyHint = "Запустите мониторинг для live-данных";

    public Array ParkingModes => Enum.GetValues(typeof(CoreParkingMode));

    /// <summary>Серия latency с Monitoring (общий сервис сэмплов).</summary>
    public System.Collections.ObjectModel.ObservableCollection<double> LatencySeries => _monitoringVm.LatencySeries;

    public DashboardViewModel(
        IProfileService profiles,
        ISafetyService safety,
        ITimerManager timer,
        IPowerManager power,
        ICoreParkingManager parking,
        IGpuManager gpu,
        ISettingsService settings,
        IBenchmarkService benchmark,
        IMonitoringService monitoring,
        MonitoringViewModel monitoringVm)
    {
        _profiles = profiles;
        _safety = safety;
        _timer = timer;
        _power = power;
        _parking = parking;
        _gpu = gpu;
        _settings = settings;
        _benchmark = benchmark;
        _monitoring = monitoring;
        _monitoringVm = monitoringVm;

        _timer.StateChanged += (_, s) =>
            App.Current.Dispatcher?.BeginInvoke(() => RefreshStatusFromSystem(s));

        _monitoring.SampleArrived += (_, s) =>
            App.Current.Dispatcher?.BeginInvoke(() =>
            {
                LiveLatencyUs = s.SchedulingLatencyUs;
                LiveTimerMs = s.TimerResolutionMs;
                LatencyHint = s.SystemUnderLoad
                    ? $"LOAD max {s.SchedulingLatencyMaxUs:F0} µs · med {s.SchedulingLatencyUs:F0}"
                    : s.SchedulingLatencyUs <= 50
                        ? "IDLE · low median (норма, если max растёт при вводе)"
                        : s.SchedulingLatencyUs <= 150
                            ? "Жёлтая · приемлемо"
                            : "Высокий median";
            }, System.Windows.Threading.DispatcherPriority.Background);

        LoadFromActiveProfile();
        RefreshSystemInfo();
        UpdateCta();
    }

    public void LoadFromActiveProfile()
    {
        var p = _settings.Current.GetActiveProfile();
        ActiveProfileName = p.Name;
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

        PowerSchemeName = schemeName;
        OptimizationsActive = s.IsActive || schemeName is "High Performance" or "Ultimate Performance";
        LiveTimerMs = s.IsActive && s.ActualMs > 0 ? s.ActualMs : LiveTimerMs;
        StatusLine = s.IsActive
            ? $"Оптимизации активны · таймер {s.ActualMs:F3} мс · {schemeName}"
            : $"Standby · таймер по умолчанию · {schemeName}";
        UpdateCta();
    }

    private void UpdateCta()
    {
        PrimaryCtaLabel = OptimizationsActive ? "RE-CALIBRATE SYSTEM" : "ENABLE OPTIMIZATION";
    }

    partial void OnOptimizationsActiveChanged(bool value) => UpdateCta();

    /// <summary>Master CTA: Apply when off, re-apply active profile when on (or Reset via secondary).</summary>
    [RelayCommand]
    private async Task ToggleMasterAsync()
    {
        if (OptimizationsActive)
        {
            // Re-calibrate = apply active profile / current toggles again
            await ApplyAsync();
        }
        else
        {
            await ApplyAsync();
        }
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
            // Prefer named active profile if custom toggles match load
            var active = _settings.Current.GetActiveProfile();
            profile.Name = active.Name;
            profile.MemoryCleanupExclusions = active.MemoryCleanupExclusions;
            profile.GameExecutables = active.GameExecutables;

            var result = await _profiles.ApplyAsync(profile);
            StatusMessage = result.Message;
            OptimizationsActive = result.Success || _timer.CurrentState.IsActive;
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
                var preset = OptimizationProfile.CreatePreset(result.Value.RecommendedProfile);
                EnableTimer = preset.EnableTimer;
                TimerTargetMs = preset.TimerTargetMs;
                EnablePowerScheme = preset.EnablePowerScheme;
                EnableCoreParking = preset.EnableCoreParkingControl;
                ParkingMode = preset.CoreParkingMode;
                EnableGameMode = preset.EnableGameModeTweak;
                EnableHags = preset.EnableHags;
                EnableGpuLowLatency = preset.EnableGpuLowLatency;
                ActiveProfileName = preset.Name;

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
