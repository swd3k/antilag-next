using System.Windows;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace AntiLagNext.App.ViewModels;

/// <summary>Настройки приложения: тема, restore point, мониторинг, tray, game auto-switch.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    [ObservableProperty] private AppTheme _theme;
    [ObservableProperty] private bool _createRestorePoint = true;
    [ObservableProperty] private bool _monitoringEnabled = true;
    [ObservableProperty] private int _monitoringIntervalMs = 500;
    [ObservableProperty] private bool _gameAutoSwitchEnabled = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _releaseTimerOnExit = true;
    [ObservableProperty] private int _maxBackupsToKeep = 20;

    public Array Themes => Enum.GetValues(typeof(AppTheme));

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        var s = settings.Current;
        Theme = s.Theme;
        CreateRestorePoint = s.CreateRestorePoint;
        MonitoringEnabled = s.MonitoringEnabled;
        MonitoringIntervalMs = s.MonitoringIntervalMs;
        GameAutoSwitchEnabled = s.GameAutoSwitchEnabled;
        MinimizeToTray = s.MinimizeToTray;
        ReleaseTimerOnExit = s.ReleaseTimerOnExit;
        MaxBackupsToKeep = s.MaxBackupsToKeep;
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Current;
        s.Theme = Theme;
        s.CreateRestorePoint = CreateRestorePoint;
        s.MonitoringEnabled = MonitoringEnabled;
        s.MonitoringIntervalMs = Math.Clamp(MonitoringIntervalMs, 100, 10_000);
        s.GameAutoSwitchEnabled = GameAutoSwitchEnabled;
        s.MinimizeToTray = MinimizeToTray;
        s.ReleaseTimerOnExit = ReleaseTimerOnExit;
        s.MaxBackupsToKeep = Math.Clamp(MaxBackupsToKeep, 1, 200);
        var r = _settings.Save();
        ApplyTheme(Theme);

        try
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel main)
                main.StartGameDetectionIfEnabled();
        }
        catch { /* best-effort */ }

        StatusMessage = r.Message;
    }

    public static void ApplyTheme(AppTheme theme)
    {
        try
        {
            var palette = new PaletteHelper();
            var current = palette.GetTheme();
            current.SetBaseTheme(theme == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);
            palette.SetTheme(current);
        }
        catch
        {
            // MaterialDesign theme switch best-effort
        }
    }
}
