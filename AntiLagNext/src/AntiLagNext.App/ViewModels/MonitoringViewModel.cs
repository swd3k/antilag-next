using System.Collections.ObjectModel;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AntiLagNext.App.ViewModels;

/// <summary>Реалтайм-мониторинг scheduling latency / timer / CPU / RAM.</summary>
public partial class MonitoringViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring;
    private readonly ISettingsService _settings;
    private const int MaxSamples = 120;

    public ObservableCollection<MonitoringSample> Samples { get; } = new();

    [ObservableProperty] private double _latestLatencyUs;
    [ObservableProperty] private double _latestTimerMs;
    [ObservableProperty] private float _latestCpu;
    [ObservableProperty] private float _latestMemMb;
    [ObservableProperty] private string _powerSourceText = "—";
    [ObservableProperty] private bool _isRunning;

    public MonitoringViewModel(IMonitoringService monitoring, ISettingsService settings)
    {
        _monitoring = monitoring;
        _settings = settings;
        _monitoring.SampleArrived += OnSample;
    }

    private void OnSample(object? sender, MonitoringSample s)
    {
        // UI-поток
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LatestLatencyUs = s.SchedulingLatencyUs;
            LatestTimerMs = s.TimerResolutionMs;
            LatestCpu = s.CpuUsagePercent;
            LatestMemMb = s.UsedMemoryMb;
            PowerSourceText = s.PowerSource == Core.Enums.PowerSource.Ac ? "Сеть (AC)" : "Батарея (DC)";

            Samples.Add(s);
            while (Samples.Count > MaxSamples)
                Samples.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void Start()
    {
        var ms = Math.Clamp(_settings.Current.MonitoringIntervalMs, 100, 5000);
        _monitoring.Start(TimeSpan.FromMilliseconds(ms));
        IsRunning = true;
        StatusMessage = $"Мониторинг каждые {ms} мс";
    }

    [RelayCommand]
    private void Stop()
    {
        _monitoring.Stop();
        IsRunning = false;
        StatusMessage = "Мониторинг остановлен";
    }
}
