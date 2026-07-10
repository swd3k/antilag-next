using System.Collections.ObjectModel;
using System.Diagnostics;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Localization;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AntiLagNext.App.ViewModels;

/// <summary>
/// Реалтайм-мониторинг. UI throttle ~8 Hz (меньше self-noise).
/// Dual story: IDLE baseline (sticky) + NOW max — interactive spikes нормальны.
/// </summary>
public partial class MonitoringViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;

    private const int MaxSamples = 240;
    private const int MaxSpikes = 80;
    public const double GreenThresholdUs = 50;
    public const double YellowThresholdUs = 150;
    private const int SustainedRedTrigger = 3;
    private const double SpikeLogThresholdUs = YellowThresholdUs;
    private const int UiMinIntervalMs = 120;

    private int _consecutiveRed;
    private DateTime? _sessionStarted;
    private double _lastLoggedSpikeUs;
    private DateTime _lastSpikeLogUtc = DateTime.MinValue;
    private DateTime _lastProcessHintUtc = DateTime.MinValue;
    private DateTime _lastUiPushUtc = DateTime.MinValue;
    private string? _cachedProcessHint;

    private int _totalSamples, _totalYellow, _totalRed, _underLoadSamples;
    private readonly List<double> _allMedians = new(4096);
    private readonly List<double> _allMaxes = new(4096);
    private readonly List<double> _recentIdleMedians = new(64);
    private readonly List<double> _baselineCaptureBuffer = new(64);
    private bool _capturingBaseline;
    private MonitoringSample? _pendingSample;

    public ObservableCollection<MonitoringSample> Samples { get; } = new();
    public ObservableCollection<double> LatencySeries { get; } = new();
    public ObservableCollection<LatencySpike> Spikes { get; } = new();

    [ObservableProperty] private double _latestLatencyUs;
    [ObservableProperty] private double _latestMaxLatencyUs;
    [ObservableProperty] private double _latestMinLatencyUs;
    [ObservableProperty] private double _idleBaselineUs;
    [ObservableProperty] private double _preApplyBaselineUs;
    [ObservableProperty] private double _baselineDeltaUs;
    [ObservableProperty] private bool _hasIdleBaseline;
    [ObservableProperty] private bool _hasPreApplyBaseline;
    [ObservableProperty] private double _latestTimerMs;
    [ObservableProperty] private float _latestCpu;
    [ObservableProperty] private float _latestMemMb;
    [ObservableProperty] private string _powerSourceText = "—";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _peakLatencyUs;
    [ObservableProperty] private double _avgLatencyUs;
    [ObservableProperty] private double _p99LatencyUs;
    [ObservableProperty] private string _latencyQuality = "—";
    [ObservableProperty] private string _sessionSummary = "Нет данных. Нажмите «Старт».";
    [ObservableProperty] private string _alertBanner = string.Empty;
    [ObservableProperty] private bool _hasActiveAlert;
    [ObservableProperty] private int _spikeCount;
    [ObservableProperty] private double _percentInRed;
    [ObservableProperty] private bool _alertsEnabled = true;
    [ObservableProperty] private bool _systemUnderLoad;
    [ObservableProperty] private string _loadContextText = "IDLE";
    [ObservableProperty] private string _baselineText = "Baseline: — (нужен покой 2–3 с)";
    [ObservableProperty] private string _metricDisclaimer =
        "Scheduling latency (timer proxy) · не FPS и не network ping. Idle↓ норма; max↑ при мыши/UI — ожидаемо.";

    [ObservableProperty] private string _behaviorHint =
        "После Apply смотрите IDLE baseline (должен снизиться). Рост NOW/MAX при движении мыши — нагрузка DPC/DWM, не «сломанный» профиль.";

    [ObservableProperty] private string _labelStart = "START";
    [ObservableProperty] private string _labelStop = "STOP";
    [ObservableProperty] private string _labelClear = "CLEAR";
    [ObservableProperty] private string _labelAlertsOn = "ALERTS ON";
    [ObservableProperty] private string _labelAlertsOff = "ALERTS OFF";
    [ObservableProperty] private string _labelRunning = "MONITORING RUNNING";
    [ObservableProperty] private string _labelDismiss = "DISMISS";
    [ObservableProperty] private string _labelSession = "SESSION";
    [ObservableProperty] private string _labelChartTitle = "Latency history";
    [ObservableProperty] private string _labelChartSub = "MEDIAN · UI ~8 Hz";
    [ObservableProperty] private string _labelMedian = "MEDIAN";
    [ObservableProperty] private string _labelMaxNow = "MAX NOW";
    [ObservableProperty] private string _labelIdleBase = "IDLE BASE";
    [ObservableProperty] private string _labelPeakP99 = "PEAK / P99";
    [ObservableProperty] private string _labelLogTitle = "HIGH LATENCY LOG";
    [ObservableProperty] private string _labelSpikesEmpty = "No high-latency events yet.";
    [ObservableProperty] private string _labelChartWaiting = "Waiting for samples…";

    public string AlertsToggleLabel => AlertsEnabled ? LabelAlertsOn : LabelAlertsOff;

    public MonitoringViewModel(IMonitoringService monitoring, ISettingsService settings, ILocalizationService loc)
    {
        _monitoring = monitoring;
        _settings = settings;
        _loc = loc;
        _loc.CultureChanged += (_, _) => RefreshLocalization();
        RefreshLocalization();
        _monitoring.SampleArrived += OnSample;
    }

    public void RefreshLocalization()
    {
        LabelStart = _loc.T("mon.start");
        LabelStop = _loc.T("mon.stop");
        LabelClear = _loc.T("mon.clear");
        LabelAlertsOn = _loc.T("mon.alerts.on");
        LabelAlertsOff = _loc.T("mon.alerts.off");
        LabelRunning = _loc.T("mon.running");
        LabelDismiss = _loc.T("mon.dismiss");
        LabelSession = _loc.T("mon.session");
        LabelChartTitle = _loc.T("mon.chart.title");
        LabelChartSub = _loc.T("mon.chart.sub");
        LabelMedian = _loc.T("mon.median");
        LabelMaxNow = _loc.T("mon.max.now");
        LabelIdleBase = _loc.T("mon.idle.base");
        LabelPeakP99 = _loc.T("mon.peak.p99");
        LabelLogTitle = _loc.T("mon.log.title");
        LabelSpikesEmpty = _loc.T("mon.spikes.empty");
        LabelChartWaiting = _loc.T("chart.waiting");
        MetricDisclaimer = _loc.T("dash.disclaimer");
        OnPropertyChanged(nameof(AlertsToggleLabel));
    }

    partial void OnAlertsEnabledChanged(bool value) => OnPropertyChanged(nameof(AlertsToggleLabel));

    private void OnSample(object? sender, MonitoringSample s)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        // Throttle UI: probe-поток не ждёт; обновляем UI не чаще ~8 Hz
        lock (this)
        {
            _pendingSample = s;
            if (_capturingBaseline && !s.SystemUnderLoad)
                _baselineCaptureBuffer.Add(s.SchedulingLatencyUs);
        }

        var now = DateTime.UtcNow;
        if ((now - _lastUiPushUtc).TotalMilliseconds < UiMinIntervalMs)
            return;

        _lastUiPushUtc = now;
        _ = dispatcher.BeginInvoke(() =>
        {
            MonitoringSample? sample;
            lock (this)
            {
                sample = _pendingSample;
                _pendingSample = null;
            }
            if (sample != null)
                ApplySampleToUi(sample);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ApplySampleToUi(MonitoringSample s)
    {
        LatestLatencyUs = s.SchedulingLatencyUs;
        LatestMaxLatencyUs = s.SchedulingLatencyMaxUs;
        LatestMinLatencyUs = s.SchedulingLatencyMinUs;
        LatestTimerMs = s.TimerResolutionMs;
        LatestCpu = s.CpuUsagePercent;
        LatestMemMb = s.UsedMemoryMb;
        PowerSourceText = s.PowerSource == Core.Enums.PowerSource.Ac ? "AC" : "DC";
        SystemUnderLoad = s.SystemUnderLoad;
        LoadContextText = s.SystemUnderLoad
            ? $"LOAD · med {s.SchedulingLatencyUs:F0} · max {s.SchedulingLatencyMaxUs:F0} µs"
            : $"IDLE · med {s.SchedulingLatencyUs:F0} · max {s.SchedulingLatencyMaxUs:F0} µs";

        // Rolling idle baseline (sticky smoothed)
        if (!s.SystemUnderLoad && s.SchedulingLatencyUs < 500)
        {
            _recentIdleMedians.Add(s.SchedulingLatencyUs);
            while (_recentIdleMedians.Count > 24)
                _recentIdleMedians.RemoveAt(0);

            if (_recentIdleMedians.Count >= 4)
            {
                var sorted = _recentIdleMedians.OrderBy(x => x).ToArray();
                double med = sorted[sorted.Length / 2];
                if (!HasIdleBaseline)
                {
                    IdleBaselineUs = med;
                    HasIdleBaseline = true;
                }
                else
                {
                    // EMA — не прыгает от одного sample
                    IdleBaselineUs = IdleBaselineUs * 0.85 + med * 0.15;
                }

                BaselineText = HasPreApplyBaseline
                    ? $"IDLE baseline {IdleBaselineUs:F0} µs · vs pre-apply {PreApplyBaselineUs:F0} ({FormatDelta(BaselineDeltaUs)})"
                    : $"IDLE baseline {IdleBaselineUs:F0} µs · (в покое, без мыши)";
            }
        }

        Samples.Add(s);
        LatencySeries.Add(s.SchedulingLatencyUs);
        while (Samples.Count > MaxSamples)
        {
            Samples.RemoveAt(0);
            if (LatencySeries.Count > 0) LatencySeries.RemoveAt(0);
        }

        _totalSamples++;
        _allMedians.Add(s.SchedulingLatencyUs);
        _allMaxes.Add(s.SchedulingLatencyMaxUs);
        if (s.SystemUnderLoad) _underLoadSamples++;

        var zone = ClassifyZone(Math.Max(s.SchedulingLatencyUs, s.SchedulingLatencyMaxUs * 0.85));
        if (zone == LatencyZone.Yellow) _totalYellow++;
        if (zone == LatencyZone.Red) _totalRed++;

        PeakLatencyUs = Math.Max(PeakLatencyUs, s.SchedulingLatencyMaxUs);
        if (LatencySeries.Count > 0)
        {
            AvgLatencyUs = LatencySeries.Average();
            P99LatencyUs = Percentile(LatencySeries, 0.99);
            LatencyQuality = ZoneToQuality(ClassifyZone(s.SchedulingLatencyUs), s.SystemUnderLoad);
        }

        ProcessSpikeDetection(s, zone);
        RefreshSessionSummary();
    }

    private static string FormatDelta(double d)
    {
        if (Math.Abs(d) < 0.5) return "±0";
        return d < 0 ? $"{d:F0} µs ✓" : $"+{d:F0} µs";
    }

    /// <summary>
    /// Снять pre-apply baseline (2–3 с покоя) → Apply → capture post baseline.
    /// Вызывается с Dashboard.
    /// </summary>
    public async Task<string> CaptureBaselinesAroundAsync(Func<Task<string>> applyAction, CancellationToken ct = default)
    {
        EnsureRunning();
        StatusMessage = "Baseline: не двигайте мышь 2.5 с…";
        double? pre = await CaptureQuietBaselineAsync(TimeSpan.FromSeconds(2.5), ct);
        if (pre is { } preVal)
        {
            PreApplyBaselineUs = preVal;
            HasPreApplyBaseline = true;
        }

        string applyMsg = await applyAction();

        StatusMessage = "После Apply: снова покой 3 с для baseline…";
        double? post = await CaptureQuietBaselineAsync(TimeSpan.FromSeconds(3), ct);
        if (post is { } postVal)
        {
            IdleBaselineUs = postVal;
            HasIdleBaseline = true;
            if (HasPreApplyBaseline)
            {
                BaselineDeltaUs = postVal - PreApplyBaselineUs;
                BaselineText =
                    $"IDLE baseline {postVal:F0} µs · was {PreApplyBaselineUs:F0} ({FormatDelta(BaselineDeltaUs)})";
            }
            else
            {
                BaselineText = $"IDLE baseline {postVal:F0} µs (post-apply)";
            }
        }

        string deltaPart = HasPreApplyBaseline && post is not null
            ? $" · idle {PreApplyBaselineUs:F0}→{IdleBaselineUs:F0} µs ({FormatDelta(BaselineDeltaUs)})"
            : "";
        StatusMessage = applyMsg + deltaPart;
        return StatusMessage;
    }

    public void EnsureRunning()
    {
        if (IsRunning) return;
        Start();
    }

    private async Task<double?> CaptureQuietBaselineAsync(TimeSpan window, CancellationToken ct)
    {
        lock (this)
        {
            _baselineCaptureBuffer.Clear();
            _capturingBaseline = true;
        }

        try
        {
            await Task.Delay(window, ct);
        }
        catch (OperationCanceledException)
        {
            lock (this) _capturingBaseline = false;
            return null;
        }

        List<double> buf;
        lock (this)
        {
            _capturingBaseline = false;
            buf = _baselineCaptureBuffer.ToList();
            _baselineCaptureBuffer.Clear();
        }

        if (buf.Count < 3)
        {
            // fallback: use recent idle
            if (_recentIdleMedians.Count >= 3)
                return Percentile(_recentIdleMedians, 0.5);
            return null;
        }

        return Percentile(buf, 0.5);
    }

    private void ProcessSpikeDetection(MonitoringSample s, LatencyZone zone)
    {
        double signal = Math.Max(s.SchedulingLatencyUs, s.SchedulingLatencyMaxUs);

        if (ClassifyZone(signal) == LatencyZone.Red)
            _consecutiveRed++;
        else
            _consecutiveRed = 0;

        bool isSpikeCandidate = signal >= SpikeLogThresholdUs;
        bool sustainedRed = _consecutiveRed >= SustainedRedTrigger;
        bool cooledDown = (DateTime.UtcNow - _lastSpikeLogUtc).TotalMilliseconds >= 500;
        bool significantlyHigher = signal >= _lastLoggedSpikeUs * 1.2 || signal >= _lastLoggedSpikeUs + 80;

        if (!isSpikeCandidate && !sustainedRed)
        {
            if (zone == LatencyZone.Green && HasActiveAlert && !s.SystemUnderLoad)
            {
                HasActiveAlert = false;
                AlertBanner = string.Empty;
            }
            return;
        }

        if (!(cooledDown || significantlyHigher))
            return;

        var spike = new LatencySpike
        {
            Timestamp = s.Timestamp.ToLocalTime(),
            LatencyUs = signal,
            Zone = ClassifyZone(signal) == LatencyZone.Green ? LatencyZone.Yellow : ClassifyZone(signal),
            CpuPercent = s.CpuUsagePercent,
            TopProcessHint = s.SystemUnderLoad ? GetTopProcessHintCached() : null,
            SustainedRedCount = _consecutiveRed
        };

        Spikes.Insert(0, spike);
        while (Spikes.Count > MaxSpikes) Spikes.RemoveAt(Spikes.Count - 1);

        SpikeCount = Spikes.Count;
        _lastSpikeLogUtc = DateTime.UtcNow;
        _lastLoggedSpikeUs = signal;

        if (AlertsEnabled && (ClassifyZone(signal) == LatencyZone.Red || sustainedRed))
        {
            HasActiveAlert = true;
            string hint = string.IsNullOrEmpty(spike.TopProcessHint) ? "" : $" · {spike.TopProcessHint}";
            string load = s.SystemUnderLoad ? " [LOAD]" : " [IDLE]";
            AlertBanner = sustainedRed
                ? $"Sustained high{load}: max {signal:F0} µs ×{_consecutiveRed}{hint}"
                : $"High latency{load}: max {signal:F0} µs{hint}";
            StatusMessage = AlertBanner;
        }
    }

    private void RefreshSessionSummary()
    {
        if (_totalSamples == 0)
        {
            SessionSummary = "Нет данных. Нажмите «Старт».";
            PercentInRed = 0;
            return;
        }

        double p99 = Percentile(_allMedians, 0.99);
        double med = Percentile(_allMedians, 0.5);
        double max = _allMaxes.Count > 0 ? _allMaxes.Max() : 0;
        PercentInRed = 100.0 * _totalRed / _totalSamples;
        double pctLoad = 100.0 * _underLoadSamples / _totalSamples;
        var dur = _sessionStarted is { } t ? DateTime.UtcNow - t : TimeSpan.Zero;
        string basePart = HasIdleBaseline ? $" · idleBase {IdleBaselineUs:F0}" : "";

        SessionSummary =
            $"{dur:mm\\:ss} · n={_totalSamples} · med~{med:F0} · p99~{p99:F0} · max {max:F0} µs" +
            $"{basePart} · red {PercentInRed:F0}% · load {pctLoad:F0}% · spikes {Spikes.Count}";
    }

    private static LatencyZone ClassifyZone(double us)
    {
        if (us <= GreenThresholdUs) return LatencyZone.Green;
        if (us <= YellowThresholdUs) return LatencyZone.Yellow;
        return LatencyZone.Red;
    }

    private static string ZoneToQuality(LatencyZone z, bool load) => z switch
    {
        LatencyZone.Green when !load => "IDLE · green",
        LatencyZone.Green => "LOAD · green med",
        LatencyZone.Yellow => load ? "LOAD · yellow" : "IDLE · yellow",
        _ => load ? "LOAD · high max — смотрите baseline" : "IDLE · high (неожиданно)"
    };

    private static double Percentile(IReadOnlyList<double> data, double p)
    {
        if (data.Count == 0) return 0;
        var sorted = data.OrderBy(x => x).ToArray();
        double idx = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(idx);
        int hi = (int)Math.Ceiling(idx);
        if (lo == hi) return sorted[lo];
        double t = idx - lo;
        return sorted[lo] * (1 - t) + sorted[hi] * t;
    }

    private string? GetTopProcessHintCached()
    {
        if ((DateTime.UtcNow - _lastProcessHintUtc).TotalSeconds < 4 && _cachedProcessHint != null)
            return _cachedProcessHint;

        _lastProcessHintUtc = DateTime.UtcNow;
        _cachedProcessHint = TryGetTopProcessHint();
        return _cachedProcessHint;
    }

    private static string? TryGetTopProcessHint()
    {
        try
        {
            Process? top = null;
            long topWs = 0;
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self) continue;
                    long ws = p.WorkingSet64;
                    if (ws > topWs)
                    {
                        topWs = ws;
                        top?.Dispose();
                        top = p;
                        continue;
                    }
                }
                catch { /* access denied */ }
                try { p.Dispose(); } catch { /* ignore */ }
            }

            if (top == null) return null;
            string name = top.ProcessName;
            double mb = topWs / (1024.0 * 1024.0);
            try { top.Dispose(); } catch { /* ignore */ }
            return $"{name}.exe ~{mb:F0} MB";
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void Start()
    {
        if (_sessionStarted is null)
            _sessionStarted = DateTime.UtcNow;

        var ms = Math.Clamp(_settings.Current.MonitoringIntervalMs, 150, 5000);
        _monitoring.Start(TimeSpan.FromMilliseconds(ms));
        IsRunning = true;
        StatusMessage = $"Probe HiPri · {ms} ms · 5×1ms · dual: idle baseline + max";
    }

    [RelayCommand]
    private void Stop()
    {
        _monitoring.Stop();
        IsRunning = false;
        RefreshSessionSummary();
        StatusMessage = _loc.T("mon.stopped") + " · " + SessionSummary;
    }

    [RelayCommand]
    private void Clear()
    {
        Samples.Clear();
        LatencySeries.Clear();
        Spikes.Clear();
        _allMedians.Clear();
        _allMaxes.Clear();
        _recentIdleMedians.Clear();
        _totalSamples = _totalYellow = _totalRed = _underLoadSamples = 0;
        _consecutiveRed = 0;
        _sessionStarted = IsRunning ? DateTime.UtcNow : null;
        PeakLatencyUs = AvgLatencyUs = P99LatencyUs = 0;
        IdleBaselineUs = PreApplyBaselineUs = BaselineDeltaUs = 0;
        HasIdleBaseline = HasPreApplyBaseline = false;
        SpikeCount = 0;
        PercentInRed = 0;
        LatencyQuality = "—";
        HasActiveAlert = false;
        AlertBanner = string.Empty;
        SystemUnderLoad = false;
        LoadContextText = "IDLE";
        BaselineText = "Baseline: — (нужен покой 2–3 с)";
        SessionSummary = "Нет данных. Нажмите «Старт».";
        StatusMessage = "Cleared";
    }

    [RelayCommand]
    private void DismissAlert()
    {
        HasActiveAlert = false;
        AlertBanner = string.Empty;
    }

    [RelayCommand]
    private void ToggleAlerts()
    {
        AlertsEnabled = !AlertsEnabled;
        StatusMessage = AlertsEnabled ? "Alerts ON" : "Alerts OFF";
        if (!AlertsEnabled)
        {
            HasActiveAlert = false;
            AlertBanner = string.Empty;
        }
    }
}
