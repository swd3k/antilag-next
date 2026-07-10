using System.Windows;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Localization;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>Главная панель — dual latency (idle baseline + max) + toggles.</summary>
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
    private readonly ILocalizationService _loc;

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
    [ObservableProperty] private double _liveMaxLatencyUs;
    [ObservableProperty] private double _liveTimerMs;
    [ObservableProperty] private double _idleBaselineUs;
    [ObservableProperty] private bool _hasIdleBaseline;
    [ObservableProperty] private string _primaryCtaLabel = "ENABLE OPTIMIZATION";
    [ObservableProperty] private string _latencyHint = "";
    [ObservableProperty] private string _baselineHint = "";
    [ObservableProperty] private string _maxHint = "";
    [ObservableProperty] private string _metricDisclaimer = "";
    [ObservableProperty] private string _labelIdle = "IDLE BASELINE";
    [ObservableProperty] private string _labelMax = "NOW MAX";
    [ObservableProperty] private string _labelTimer = "TIMER · PROFILE";
    [ObservableProperty] private string _labelSuccess = "SUCCESS";
    [ObservableProperty] private string _labelLive = "LIVE";
    [ObservableProperty] private string _labelReset = "RESET ALL";
    [ObservableProperty] private string _labelReady = "SYSTEM READY";
    [ObservableProperty] private string _labelOptimized = "OPTIMIZED";
    [ObservableProperty] private string _labelChartTitle = "Latency History";
    [ObservableProperty] private string _labelChartSub = "MEDIAN · SCHEDULING PROXY";
    [ObservableProperty] private string _labelChartHint = "";
    [ObservableProperty] private string _labelToggles = "OPTIMIZATION TOGGLES";
    [ObservableProperty] private string _labelTogglesSub = "Apply / Re-calibrate";
    [ObservableProperty] private string _labelBench = "BENCH";
    [ObservableProperty] private string _labelToggleTimer = "Timer resolution";
    [ObservableProperty] private string _labelTogglePower = "High Performance";
    [ObservableProperty] private string _labelToggleParking = "Core parking";
    [ObservableProperty] private string _labelToggleGameMode = "Game Mode / DVR";
    [ObservableProperty] private string _labelToggleHags = "HAGS";
    [ObservableProperty] private string _labelToggleGpu = "GPU Low Latency";
    [ObservableProperty] private string _labelToggleMemory = "Memory trim";
    [ObservableProperty] private string _zoneGreen = "≤50 µs green";
    [ObservableProperty] private string _zoneYellow = "≤150 yellow";
    [ObservableProperty] private string _zoneRed = ">150 red";
    [ObservableProperty] private string _labelChartWaiting = "Waiting for samples…";

    public Array ParkingModes => Enum.GetValues(typeof(CoreParkingMode));

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
        MonitoringViewModel monitoringVm,
        ILocalizationService loc)
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
        _loc = loc;
        RefreshLocalization();

        _timer.StateChanged += (_, s) =>
            App.Current.Dispatcher?.BeginInvoke(() => RefreshStatusFromSystem(s));

        _monitoring.SampleArrived += (_, s) =>
            App.Current.Dispatcher?.BeginInvoke(() =>
            {
                LiveLatencyUs = s.SchedulingLatencyUs;
                LiveMaxLatencyUs = s.SchedulingLatencyMaxUs;
                LiveTimerMs = s.TimerResolutionMs;
                IdleBaselineUs = _monitoringVm.IdleBaselineUs;
                HasIdleBaseline = _monitoringVm.HasIdleBaseline;

                if (_monitoringVm.HasIdleBaseline)
                {
                    BaselineHint = _monitoringVm.HasPreApplyBaseline
                        ? $"Idle {_monitoringVm.IdleBaselineUs:F0} µs · Δ {_monitoringVm.BaselineDeltaUs:F0}"
                        : $"Idle {_monitoringVm.IdleBaselineUs:F0} µs (покой)";
                }

                LatencyHint = s.SystemUnderLoad
                    ? $"LOAD med {s.SchedulingLatencyUs:F0}"
                    : $"IDLE med {s.SchedulingLatencyUs:F0}";
                MaxHint = s.SystemUnderLoad
                    ? $"MAX {s.SchedulingLatencyMaxUs:F0} µs · input/UI load"
                    : $"MAX {s.SchedulingLatencyMaxUs:F0} µs · low load";
            }, System.Windows.Threading.DispatcherPriority.Background);

        LoadFromActiveProfile();
        RefreshSystemInfo();
        UpdateCta();

        // Live metrics with app
        if (_settings.Current.MonitoringEnabled)
            _monitoringVm.EnsureRunning();
    }

    public void LoadFromActiveProfile()
    {
        var p = _settings.Current.GetActiveProfile();
        // Prefer i18n pack; fall back to culture-aware built-in labels (never leave raw RU Name on EN UI)
        string key = OptimizationProfile.I18nKey(p.Kind);
        string viaLoc = _loc.T(key);
        ActiveProfileName = !string.IsNullOrWhiteSpace(viaLoc) && viaLoc != key
            ? viaLoc
            : OptimizationProfile.LocalizedName(p.Kind, _loc.CurrentCulture);
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
        // "Active" = we applied a profile (persisted), not "user already on High Performance"
        bool ours = ActiveStateStore.IsActive();
        OptimizationsActive = ours || s.IsActive;
        LiveTimerMs = s.IsActive && s.ActualMs > 0 ? s.ActualMs : (LiveTimerMs > 0 ? LiveTimerMs : 15.625);
        StatusLine = ours || s.IsActive
            ? $"ON · timer {(s.IsActive ? s.ActualMs : LiveTimerMs):F3} ms · {schemeName}"
            : $"Standby · {schemeName}";
        UpdateCta();
    }

    private void UpdateCta()
    {
        PrimaryCtaLabel = OptimizationsActive
            ? _loc.T("dash.cta.recal")
            : _loc.T("dash.cta.enable");
    }

    public void RefreshLocalization()
    {
        MetricDisclaimer = _loc.T("dash.disclaimer");
        LabelIdle = _loc.T("dash.idle");
        LabelMax = _loc.T("dash.max");
        LabelTimer = _loc.T("dash.timer");
        LabelSuccess = _loc.T("dash.success");
        LabelLive = _loc.T("dash.live");
        LabelReset = _loc.T("dash.reset");
        LabelReady = _loc.T("dash.ready");
        LabelOptimized = _loc.T("dash.optimized");
        LabelChartTitle = _loc.T("dash.chart.title");
        LabelChartSub = _loc.T("dash.chart.sub");
        LabelChartHint = _loc.T("dash.chart.hint");
        LabelToggles = _loc.T("dash.toggles");
        LabelTogglesSub = _loc.T("dash.toggles.sub");
        LabelBench = _loc.T("dash.bench");
        LabelToggleTimer = _loc.T("dash.toggle.timer");
        LabelTogglePower = _loc.T("dash.toggle.power");
        LabelToggleParking = _loc.T("dash.toggle.parking");
        LabelToggleGameMode = _loc.T("dash.toggle.gamemode");
        LabelToggleHags = _loc.T("dash.toggle.hags");
        LabelToggleGpu = _loc.T("dash.toggle.gpu");
        LabelToggleMemory = _loc.T("dash.toggle.memory");
        ZoneGreen = _loc.T("dash.zone.green");
        ZoneYellow = _loc.T("dash.zone.yellow");
        ZoneRed = _loc.T("dash.zone.red");
        LabelChartWaiting = _loc.T("chart.waiting");
        UpdateCta();
    }

    partial void OnOptimizationsActiveChanged(bool value) => UpdateCta();

    [RelayCommand]
    private async Task ToggleMasterAsync() => await ApplyAsync();

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = _loc.T("status.applying");
        try
        {
            _monitoringVm.EnsureRunning();

            string final = await _monitoringVm.CaptureBaselinesAroundAsync(async () =>
            {
                var profile = BuildProfileFromToggles();
                var active = _settings.Current.GetActiveProfile();
                profile.Name = active.Name;
                profile.MemoryCleanupExclusions = active.MemoryCleanupExclusions;
                profile.GameExecutables = active.GameExecutables;

                var result = await _profiles.ApplyAsync(profile);
                OptimizationsActive = result.Success || ActiveStateStore.IsActive() || _timer.CurrentState.IsActive;
                RefreshStatusFromSystem(_timer.CurrentState);
                Log.Information("Apply: {Msg}", result.Message);
                return result.Message;
            });

            StatusMessage = final;
            IdleBaselineUs = _monitoringVm.IdleBaselineUs;
            HasIdleBaseline = _monitoringVm.HasIdleBaseline;
            if (HasIdleBaseline)
            {
                BaselineHint = _monitoringVm.HasPreApplyBaseline
                    ? $"Idle {IdleBaselineUs:F0} µs · Δ {_monitoringVm.BaselineDeltaUs:F0}"
                    : $"Idle {IdleBaselineUs:F0} µs";
            }
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

        var confirm = MessageBox.Show(
            _loc.T("confirm.reset.body"),
            _loc.T("confirm.reset.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await ExecuteResetAllAsync();
    }

    /// <summary>Runs reset without confirm (caller already confirmed, e.g. tray after MessageBox).</summary>
    public Task ResetAllConfirmedAsync() => ExecuteResetAllAsync();

    private async Task ExecuteResetAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = _loc.T("status.resetting");
        try
        {
            var result = await _safety.ResetAllAsync();
            // Force UI to "off" regardless of stock power plan name
            OptimizationsActive = false;
            ActiveStateStore.MarkInactive();
            LiveTimerMs = _timer.CurrentState.IsActive ? _timer.CurrentState.ActualMs : 15.625;
            RefreshStatusFromSystem(_timer.CurrentState);
            OptimizationsActive = false; // Refresh may re-read active-state
            StatusMessage = result.Success
                ? result.Message
                : result.Message + (result.Detail is { Length: > 0 } d ? " · " + d : "");
            Log.Information("Reset: {Msg} | {Detail}", result.Message, result.Detail);
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
        StatusMessage = _loc.T("status.busy");
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
                {
                    string pk = OptimizationProfile.I18nKey(preset.Kind);
                    string pl = _loc.T(pk);
                    ActiveProfileName = !string.IsNullOrWhiteSpace(pl) && pl != pk
                        ? pl
                        : OptimizationProfile.LocalizedName(preset.Kind, _loc.CurrentCulture);
                }

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
