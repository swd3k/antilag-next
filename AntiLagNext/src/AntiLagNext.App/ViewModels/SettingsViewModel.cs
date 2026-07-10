using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using AntiLagNext.App.Services;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AntiLagNext.App.ViewModels;

/// <summary>Настройки: тема, язык, safety, tray + build identity (path).</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;

    [ObservableProperty] private AppTheme _theme;
    [ObservableProperty] private string _uiCulture = "ru";
    [ObservableProperty] private bool _createRestorePoint = true;
    [ObservableProperty] private bool _monitoringEnabled = true;
    [ObservableProperty] private int _monitoringIntervalMs = 500;
    [ObservableProperty] private bool _gameAutoSwitchEnabled = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _releaseTimerOnExit = true;
    [ObservableProperty] private int _maxBackupsToKeep = 20;
    [ObservableProperty] private string _exePath = "—";
    [ObservableProperty] private string _buildInfo = "—";
    [ObservableProperty] private ThemeOption? _selectedThemeOption;

    // Localized labels
    [ObservableProperty] private string _labelBuild = "";
    [ObservableProperty] private string _labelAppearance = "";
    [ObservableProperty] private string _labelTheme = "";
    [ObservableProperty] private string _labelLanguage = "";
    [ObservableProperty] private string _labelLanguageHint = "";
    [ObservableProperty] private string _labelSafety = "";
    [ObservableProperty] private string _labelRestore = "";
    [ObservableProperty] private string _labelRestoreDesc = "";
    [ObservableProperty] private string _labelMonitoring = "";
    [ObservableProperty] private string _labelInterval = "";
    [ObservableProperty] private string _labelGameAuto = "";
    [ObservableProperty] private string _labelGameAutoDesc = "";
    [ObservableProperty] private string _labelTray = "";
    [ObservableProperty] private string _labelTrayMin = "";
    [ObservableProperty] private string _labelTrayRelease = "";
    [ObservableProperty] private string _labelBackupsMax = "";
    [ObservableProperty] private string _labelSave = "";

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new();
    public IReadOnlyList<string> Cultures => _loc.AvailableCultures;

    public SettingsViewModel(ISettingsService settings, ILocalizationService loc)
    {
        _settings = settings;
        _loc = loc;
        var s = settings.Current;
        Theme = s.Theme;
        UiCulture = string.IsNullOrWhiteSpace(s.UiCulture) ? "ru" : s.UiCulture;
        CreateRestorePoint = s.CreateRestorePoint;
        MonitoringEnabled = s.MonitoringEnabled;
        MonitoringIntervalMs = s.MonitoringIntervalMs;
        GameAutoSwitchEnabled = s.GameAutoSwitchEnabled;
        MinimizeToTray = s.MinimizeToTray;
        ReleaseTimerOnExit = s.ReleaseTimerOnExit;
        MaxBackupsToKeep = s.MaxBackupsToKeep;
        RefreshBuildIdentity();
        RefreshLocalization();
        _loc.CultureChanged += (_, _) => RefreshLocalization();
    }

    public void RefreshLocalization()
    {
        LabelBuild = _loc.T("settings.build");
        LabelAppearance = _loc.T("settings.appearance");
        LabelTheme = _loc.T("settings.theme");
        LabelLanguage = _loc.T("settings.language");
        LabelLanguageHint = _loc.T("settings.language.hint");
        LabelSafety = _loc.T("settings.safety");
        LabelRestore = _loc.T("settings.restore");
        LabelRestoreDesc = _loc.T("settings.restore.desc");
        LabelMonitoring = _loc.T("settings.monitoring");
        LabelInterval = _loc.T("settings.interval");
        LabelGameAuto = _loc.T("settings.game.auto");
        LabelGameAutoDesc = _loc.T("settings.game.auto.desc");
        LabelTray = _loc.T("settings.tray");
        LabelTrayMin = _loc.T("settings.tray.min");
        LabelTrayRelease = _loc.T("settings.tray.release");
        LabelBackupsMax = _loc.T("settings.backups.max");
        LabelSave = _loc.T("settings.save");

        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOption(AppTheme.Dark, _loc.T("settings.theme.dark")));
        ThemeOptions.Add(new ThemeOption(AppTheme.Light, _loc.T("settings.theme.light")));
        ThemeOptions.Add(new ThemeOption(AppTheme.System, _loc.T("settings.theme.system")));
        SelectedThemeOption = ThemeOptions.FirstOrDefault(t => t.Value == Theme)
                              ?? ThemeOptions.FirstOrDefault();
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (value is null) return;
        Theme = value.Value;
        // Live preview — no need to Save first
        AppThemeService.Apply(Theme);
        RefreshNavBindings();
    }

    /// <summary>Nav converters cache colors; nudge CurrentNavKey so they re-read theme brushes.</summary>
    private static void RefreshNavBindings()
    {
        try
        {
            if (Application.Current?.MainWindow?.DataContext is not MainViewModel main) return;
            var key = main.CurrentNavKey;
            main.CurrentNavKey = string.Empty;
            main.CurrentNavKey = key;
        }
        catch { /* best-effort */ }
    }

    private void RefreshBuildIdentity()
    {
        try
        {
            ExePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? AppContext.BaseDirectory;

            string dll = Path.Combine(AppContext.BaseDirectory, "AntiLagNext.dll");
            string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            string mtime = File.Exists(dll)
                ? File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss")
                : "?";
            BuildInfo = $"v{ver} · DLL {mtime}";
        }
        catch
        {
            ExePath = AppContext.BaseDirectory;
            BuildInfo = "unknown";
        }
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Current;
        s.Theme = Theme;
        s.UiCulture = UiCulture;
        s.CreateRestorePoint = CreateRestorePoint;
        s.MonitoringEnabled = MonitoringEnabled;
        s.MonitoringIntervalMs = Math.Clamp(MonitoringIntervalMs, 100, 10_000);
        s.GameAutoSwitchEnabled = GameAutoSwitchEnabled;
        s.MinimizeToTray = MinimizeToTray;
        s.ReleaseTimerOnExit = ReleaseTimerOnExit;
        s.MaxBackupsToKeep = Math.Clamp(MaxBackupsToKeep, 1, 200);
        var r = _settings.Save();

        AppThemeService.Apply(Theme);
        RefreshNavBindings();
        _loc.SetCulture(UiCulture);

        try
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel main)
                main.StartGameDetectionIfEnabled();
        }
        catch { /* best-effort */ }

        StatusMessage = r.Success ? _loc.T("settings.saved") : r.Message;
    }
}

/// <summary>Localized theme dropdown item.</summary>
public sealed class ThemeOption
{
    public ThemeOption(AppTheme value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public AppTheme Value { get; }
    public string DisplayName { get; }
    public override string ToString() => DisplayName;
}
