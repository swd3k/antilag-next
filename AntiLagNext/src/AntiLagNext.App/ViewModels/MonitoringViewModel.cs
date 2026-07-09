using System.Collections.ObjectModel;
using System.Diagnostics;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AntiLagNext.App.ViewModels;

/// <summary>
/// Реалтайм-мониторинг + waveform + детектор «высокого мс» (latency spikes).
/// </summary>
public partial class MonitoringViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring;
    private readonly ISettingsService _settings;

    private const int MaxSamples = 300;
    private const int MaxSpikes = 100;

    /// <summary>Пороги (µs), как на графике DPC-style.</summary>
    public const double GreenThresholdUs = 50;
    public const double YellowThresholdUs = 150;

    /// <summary>Сколько подряд red-сэмплов = «устойчивый» высокий latency.</summary>
    private const int SustainedRedTrigger = 3;

    /// <summary>Минимум µs, чтобы записать одиночный spike (жёлтый/красный).</summary>
    private const double SpikeLogThresholdUs = YellowThresholdUs;

    private int _consecutiveRed;
    private DateTime? _sessionStarted;
    private double _lastLoggedSpikeUs;
    private DateTime _lastSpikeLogUtc = DateTime.MinValue;

    // session counters (all samples, not just rolling window)
    private int _totalSamples;
    private int _totalYellow;
    private int _totalRed;
    private readonly List<double> _allLatencies = new(4096);

    public ObservableCollection<MonitoringSample> Samples { get; } = new();
    public ObservableCollection<double> LatencySeries { get; } = new();
    public ObservableCollection<LatencySpike> Spikes { get; } = new();

    [ObservableProperty] private double _latestLatencyUs;
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

    public MonitoringViewModel(IMonitoringService monitoring, ISettingsService settings)
    {
        _monitoring = monitoring;
        _settings = settings;
        _monitoring.SampleArrived += OnSample;
    }

    private void OnSample(object? sender, MonitoringSample s)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LatestLatencyUs = s.SchedulingLatencyUs;
            LatestTimerMs = s.TimerResolutionMs;
            LatestCpu = s.CpuUsagePercent;
            LatestMemMb = s.UsedMemoryMb;
            PowerSourceText = s.PowerSource == Core.Enums.PowerSource.Ac ? "Сеть (AC)" : "Батарея (DC)";

            Samples.Add(s);
            LatencySeries.Add(s.SchedulingLatencyUs);
            while (Samples.Count > MaxSamples)
            {
                Samples.RemoveAt(0);
                if (LatencySeries.Count > 0)
                    LatencySeries.RemoveAt(0);
            }

            // Session stats
            _totalSamples++;
            _allLatencies.Add(s.SchedulingLatencyUs);
            var zone = ClassifyZone(s.SchedulingLatencyUs);
            if (zone == LatencyZone.Yellow) _totalYellow++;
            if (zone == LatencyZone.Red) _totalRed++;

            if (LatencySeries.Count > 0)
            {
                PeakLatencyUs = Math.Max(PeakLatencyUs, s.SchedulingLatencyUs);
                AvgLatencyUs = LatencySeries.Average();
                P99LatencyUs = Percentile(LatencySeries, 0.99);
                LatencyQuality = ZoneToQuality(zone);
            }

            ProcessSpikeDetection(s, zone);
            RefreshSessionSummary();
        });
    }

    private void ProcessSpikeDetection(MonitoringSample s, LatencyZone zone)
    {
        if (zone == LatencyZone.Red)
            _consecutiveRed++;
        else
            _consecutiveRed = 0;

        bool isSpikeCandidate = s.SchedulingLatencyUs >= SpikeLogThresholdUs;
        bool sustainedRed = _consecutiveRed >= SustainedRedTrigger;

        // Debounce: не логировать почти тот же пик каждые 200 мс
        bool cooledDown = (DateTime.UtcNow - _lastSpikeLogUtc).TotalMilliseconds >= 400;
        bool significantlyHigher = s.SchedulingLatencyUs >= _lastLoggedSpikeUs * 1.15
                                   || s.SchedulingLatencyUs >= _lastLoggedSpikeUs + 50;

        if (!isSpikeCandidate && !sustainedRed)
        {
            if (zone == LatencyZone.Green && HasActiveAlert)
            {
                HasActiveAlert = false;
                AlertBanner = string.Empty;
            }
            return;
        }

        if (!(cooledDown || significantlyHigher || sustainedRed && cooledDown))
            return;

        // Log spike event
        var spike = new LatencySpike
        {
            Timestamp = s.Timestamp.ToLocalTime(),
            LatencyUs = s.SchedulingLatencyUs,
            Zone = zone == LatencyZone.Green ? LatencyZone.Yellow : zone,
            CpuPercent = s.CpuUsagePercent,
            TopProcessHint = TryGetTopProcessHint(),
            SustainedRedCount = _consecutiveRed
        };

        Spikes.Insert(0, spike);
        while (Spikes.Count > MaxSpikes)
            Spikes.RemoveAt(Spikes.Count - 1);

        SpikeCount = Spikes.Count;
        _lastSpikeLogUtc = DateTime.UtcNow;
        _lastLoggedSpikeUs = s.SchedulingLatencyUs;

        if (AlertsEnabled && (zone == LatencyZone.Red || sustainedRed))
        {
            HasActiveAlert = true;
            string hint = string.IsNullOrEmpty(spike.TopProcessHint) ? "" : $" · процесс: {spike.TopProcessHint}";
            AlertBanner = sustainedRed
                ? $"⚠ Устойчивый высокий latency: {s.SchedulingLatencyUs:F0} µs ({spike.LatencyMs:F3} мс) ×{_consecutiveRed}{hint}"
                : $"⚠ Высокий latency: {s.SchedulingLatencyUs:F0} µs ({spike.LatencyMs:F3} мс){hint}";
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

        double p99 = Percentile(_allLatencies, 0.99);
        double med = Percentile(_allLatencies, 0.5);
        double max = _allLatencies.Max();
        double avg = _allLatencies.Average();
        PercentInRed = 100.0 * _totalRed / _totalSamples;
        double pctYellow = 100.0 * _totalYellow / _totalSamples;
        var dur = _sessionStarted is { } t ? DateTime.UtcNow - t : TimeSpan.Zero;

        SessionSummary =
            $"Сессия {dur:mm\\:ss} · сэмплов {_totalSamples} · max {max:F0} µs · p99 {p99:F0} µs · med {med:F0} µs · " +
            $"avg {avg:F0} µs · red {PercentInRed:F1}% · yellow {pctYellow:F1}% · пиков в журнале: {Spikes.Count}";
    }

    private static LatencyZone ClassifyZone(double us)
    {
        if (us <= GreenThresholdUs) return LatencyZone.Green;
        if (us <= YellowThresholdUs) return LatencyZone.Yellow;
        return LatencyZone.Red;
    }

    private static string ZoneToQuality(LatencyZone z) => z switch
    {
        LatencyZone.Green => "Отлично (зелёная зона)",
        LatencyZone.Yellow => "Приемлемо (жёлтая зона)",
        _ => "Плохо — высокий latency"
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

    /// <summary>Эвристика: процесс с наибольшим WorkingSet (не доказательство DPC-виновника).</summary>
    private static string? TryGetTopProcessHint()
    {
        try
        {
            Process? top = null;
            long topWs = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    long ws = p.WorkingSet64;
                    if (ws > topWs)
                    {
                        topWs = ws;
                        top = p;
                    }
                }
                catch { /* access denied */ }
                finally
                {
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            }

            if (top == null) return null;
            string name = top.ProcessName;
            double mb = topWs / (1024.0 * 1024.0);
            return $"{name}.exe ~{mb:F0} МБ RAM";
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

        var ms = Math.Clamp(_settings.Current.MonitoringIntervalMs, 50, 5000);
        if (ms > 200) ms = Math.Min(ms, 200);
        _monitoring.Start(TimeSpan.FromMilliseconds(ms));
        IsRunning = true;
        StatusMessage = $"Детектор высокого latency · каждые {ms} мс · yellow≥{YellowThresholdUs:F0} µs · red≥sustained";
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
        _allLatencies.Clear();
        _totalSamples = 0;
        _totalYellow = 0;
        _totalRed = 0;
        _consecutiveRed = 0;
        _sessionStarted = IsRunning ? DateTime.UtcNow : null;
        PeakLatencyUs = 0;
        AvgLatencyUs = 0;
        P99LatencyUs = 0;
        SpikeCount = 0;
        PercentInRed = 0;
        LatencyQuality = "—";
        HasActiveAlert = false;
        AlertBanner = string.Empty;
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
