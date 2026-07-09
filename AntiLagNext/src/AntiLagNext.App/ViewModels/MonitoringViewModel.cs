using System.Collections.ObjectModel;
using System.Diagnostics;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AntiLagNext.App.ViewModels;

/// <summary>
/// Реалтайм-мониторинг. UI обновляется через BeginInvoke (не блокирует probe-поток).
/// Idle-низкий + interactive-высокий — ожидаемо; график = median, Peak/max = worst case.
/// </summary>
public partial class MonitoringViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring;
    private readonly ISettingsService _settings;

    private const int MaxSamples = 300;
    private const int MaxSpikes = 100;
    public const double GreenThresholdUs = 50;
    public const double YellowThresholdUs = 150;
    private const int SustainedRedTrigger = 3;
    private const double SpikeLogThresholdUs = YellowThresholdUs;

    private int _consecutiveRed;
    private DateTime? _sessionStarted;
    private double _lastLoggedSpikeUs;
    private DateTime _lastSpikeLogUtc = DateTime.MinValue;
    private DateTime _lastProcessHintUtc = DateTime.MinValue;
    private string? _cachedProcessHint;

    private int _totalSamples, _totalYellow, _totalRed, _underLoadSamples;
    private readonly List<double> _allMedians = new(4096);
    private readonly List<double> _allMaxes = new(4096);

    public ObservableCollection<MonitoringSample> Samples { get; } = new();
    public ObservableCollection<double> LatencySeries { get; } = new();
    public ObservableCollection<LatencySpike> Spikes { get; } = new();

    [ObservableProperty] private double _latestLatencyUs;
    [ObservableProperty] private double _latestMaxLatencyUs;
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
    [ObservableProperty] private string _loadContextText = "IDLE / low load";
    [ObservableProperty] private string _behaviorHint =
        "Idle ≈ низкий latency — норма. При движении мыши/окнах/игре max растёт: это нагрузка системы, не «сломанный» AntiLag.";

    public MonitoringViewModel(IMonitoringService monitoring, ISettingsService settings)
    {
        _monitoring = monitoring;
        _settings = settings;
        _monitoring.SampleArrived += OnSample;
    }

    private void OnSample(object? sender, MonitoringSample s)
    {
        // НЕ Invoke: блокировка UI-потока искажала бы следующий замер и тормозила ПК
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        _ = dispatcher.BeginInvoke(() =>
        {
            LatestLatencyUs = s.SchedulingLatencyUs;
            LatestMaxLatencyUs = s.SchedulingLatencyMaxUs;
            LatestTimerMs = s.TimerResolutionMs;
            LatestCpu = s.CpuUsagePercent;
            LatestMemMb = s.UsedMemoryMb;
            PowerSourceText = s.PowerSource == Core.Enums.PowerSource.Ac ? "Сеть (AC)" : "Батарея (DC)";
            SystemUnderLoad = s.SystemUnderLoad;
            LoadContextText = s.SystemUnderLoad
                ? $"LOAD · med {s.SchedulingLatencyUs:F0} · max {s.SchedulingLatencyMaxUs:F0} µs · CPU {s.CpuUsagePercent:F0}%"
                : $"IDLE · med {s.SchedulingLatencyUs:F0} · max {s.SchedulingLatencyMaxUs:F0} µs";

            Samples.Add(s);
            // График: median (стабильнее). Peak берём из max.
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

            // Классификация по max — иначе interactive spikes «прячутся» в median
            var zone = ClassifyZone(Math.Max(s.SchedulingLatencyUs, s.SchedulingLatencyMaxUs * 0.85));
            if (zone == LatencyZone.Yellow) _totalYellow++;
            if (zone == LatencyZone.Red) _totalRed++;

            PeakLatencyUs = Math.Max(PeakLatencyUs, s.SchedulingLatencyMaxUs);
            if (LatencySeries.Count > 0)
            {
                AvgLatencyUs = LatencySeries.Average();
                P99LatencyUs = Percentile(LatencySeries, 0.99);
                LatencyQuality = ZoneToQuality(ClassifyZone(s.SchedulingLatencyUs));
            }

            ProcessSpikeDetection(s, zone);
            RefreshSessionSummary();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ProcessSpikeDetection(MonitoringSample s, LatencyZone zone)
    {
        // Для red/sustained смотрим worst-case max
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
                ? $"⚠ Sustained high latency{load}: {signal:F0} µs ({spike.LatencyMs:F3} мс) ×{_consecutiveRed}{hint}"
                : $"⚠ High latency{load}: {signal:F0} µs ({spike.LatencyMs:F3} мс){hint}";
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
        double avg = _allMedians.Average();
        PercentInRed = 100.0 * _totalRed / _totalSamples;
        double pctYellow = 100.0 * _totalYellow / _totalSamples;
        double pctLoad = 100.0 * _underLoadSamples / _totalSamples;
        var dur = _sessionStarted is { } t ? DateTime.UtcNow - t : TimeSpan.Zero;

        SessionSummary =
            $"Сессия {dur:mm\\:ss} · n={_totalSamples} · median~{med:F0} · p99~{p99:F0} · max {max:F0} µs · " +
            $"avg {avg:F0} · red {PercentInRed:F1}% · yellow {pctYellow:F1}% · under load {pctLoad:F0}% · spikes {Spikes.Count}";
    }

    private static LatencyZone ClassifyZone(double us)
    {
        if (us <= GreenThresholdUs) return LatencyZone.Green;
        if (us <= YellowThresholdUs) return LatencyZone.Yellow;
        return LatencyZone.Red;
    }

    private static string ZoneToQuality(LatencyZone z) => z switch
    {
        LatencyZone.Green => "Отлично (зелёная · median)",
        LatencyZone.Yellow => "Приемлемо (жёлтая)",
        _ => "Высокий median — смотрите LOAD/max"
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

    /// <summary>Кэш 3с — полный Process.GetProcesses на каждый spike жрёт CPU и сам поднимает latency.</summary>
    private string? GetTopProcessHintCached()
    {
        if ((DateTime.UtcNow - _lastProcessHintUtc).TotalSeconds < 3 && _cachedProcessHint != null)
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
            return $"{name}.exe ~{mb:F0} МБ RAM (эвристика)";
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

        var ms = Math.Clamp(_settings.Current.MonitoringIntervalMs, 80, 5000);
        // С пачкой 7×1ms проб интервал &lt;120ms почти всегда overlapping — поднимаем пол
        if (ms < 120) ms = 120;
        _monitoring.Start(TimeSpan.FromMilliseconds(ms));
        IsRunning = true;
        StatusMessage = $"Probe HiPri · period {ms} ms · 7×1ms batch · median+max · idle≠load";
    }

    [RelayCommand]
    private void Stop()
    {
        _monitoring.Stop();
        IsRunning = false;
        RefreshSessionSummary();
        StatusMessage = "Мониторинг остановлен · " + SessionSummary;
    }

    [RelayCommand]
    private void Clear()
    {
        Samples.Clear();
        LatencySeries.Clear();
        Spikes.Clear();
        _allMedians.Clear();
        _allMaxes.Clear();
        _totalSamples = _totalYellow = _totalRed = _underLoadSamples = 0;
        _consecutiveRed = 0;
        _sessionStarted = IsRunning ? DateTime.UtcNow : null;
        PeakLatencyUs = AvgLatencyUs = P99LatencyUs = 0;
        SpikeCount = 0;
        PercentInRed = 0;
        LatencyQuality = "—";
        HasActiveAlert = false;
        AlertBanner = string.Empty;
        SystemUnderLoad = false;
        LoadContextText = "IDLE / low load";
        SessionSummary = "Нет данных. Нажмите «Старт».";
        StatusMessage = "Журнал и график очищены";
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
        StatusMessage = AlertsEnabled ? "Алерты включены" : "Алерты выключены";
        if (!AlertsEnabled)
        {
            HasActiveAlert = false;
            AlertBanner = string.Empty;
        }
    }
}
