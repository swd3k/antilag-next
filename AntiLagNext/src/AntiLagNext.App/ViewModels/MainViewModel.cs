using System.Diagnostics;
using System.Windows;
using AntiLagNext.App.Services;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Localization;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>Навигация между разделами + авто-профиль по запуску игр.</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IProfileService _profileService;
    private readonly IGameDetectionService _gameDetection;
    private readonly ILocalizationService _loc;
    private int _activeGameCount;
    private Guid? _profileBeforeGame;

    public DashboardViewModel Dashboard { get; }
    public ProfilesViewModel Profiles { get; }
    public MonitoringViewModel Monitoring { get; }
    public BackupsViewModel Backups { get; }
    public PluginsViewModel Plugins { get; }
    public TipsViewModel Tips { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _currentPageTitle = "Dashboard";
    [ObservableProperty] private string _currentPageSubtitle = "";
    [ObservableProperty] private string _currentPageOverline = "";
    [ObservableProperty] private string _currentNavKey = "Dashboard";
    [ObservableProperty] private bool _showFirstRunBanner;

    // Localized chrome
    [ObservableProperty] private string _navDashboard = "Dashboard";
    [ObservableProperty] private string _navProfiles = "Games & Profiles";
    [ObservableProperty] private string _navMonitoring = "Analytics";
    [ObservableProperty] private string _navPlugins = "Plugins";
    [ObservableProperty] private string _navBackups = "Backups";
    [ObservableProperty] private string _navTips = "Tips";
    [ObservableProperty] private string _navSettings = "Settings";
    [ObservableProperty] private string _navGithub = "GitHub";
    [ObservableProperty] private string _navSystemChip = "SYSTEM";
    [ObservableProperty] private string _firstRunTitle = "FIRST RUN";
    [ObservableProperty] private string _firstRunHint = "";
    [ObservableProperty] private string _firstRunCta = "RUN BENCHMARK";

    public MainViewModel(
        DashboardViewModel dashboard,
        ProfilesViewModel profiles,
        MonitoringViewModel monitoring,
        BackupsViewModel backups,
        PluginsViewModel plugins,
        TipsViewModel tips,
        SettingsViewModel settings,
        ISettingsService settingsService,
        IProfileService profileService,
        IGameDetectionService gameDetection,
        ILocalizationService loc)
    {
        Dashboard = dashboard;
        Profiles = profiles;
        Monitoring = monitoring;
        Backups = backups;
        Plugins = plugins;
        Tips = tips;
        Settings = settings;
        _settingsService = settingsService;
        _profileService = profileService;
        _gameDetection = gameDetection;
        _loc = loc;
        _currentPage = dashboard;

        ShowFirstRunBanner = !settingsService.Current.FirstRunCompleted;
        AppThemeService.Apply(settingsService.Current.Theme);

        if (!string.IsNullOrWhiteSpace(settingsService.Current.UiCulture))
            _loc.SetCulture(settingsService.Current.UiCulture);

        _loc.CultureChanged += (_, _) =>
        {
            RefreshChromeLoc();
            Navigate(CurrentNavKey);
            Dashboard.RefreshLocalization();
            Profiles.RefreshLocalization();
            Monitoring.RefreshLocalization();
            Plugins.Reload();
            Tips.Reload();
            Backups.RefreshLocalization();
            Settings.RefreshLocalization();
        };

        RefreshChromeLoc();
        Navigate("Dashboard");

        _gameDetection.GameStarted += OnGameStarted;
        _gameDetection.GameStopped += OnGameStopped;
        StartGameDetectionIfEnabled();
    }

    private void RefreshChromeLoc()
    {
        NavDashboard = _loc.T("nav.dashboard");
        NavProfiles = _loc.T("nav.profiles");
        NavMonitoring = _loc.T("nav.monitoring");
        NavPlugins = _loc.T("nav.plugins");
        NavBackups = _loc.T("nav.backups");
        NavTips = _loc.T("nav.tips");
        NavSettings = _loc.T("nav.settings");
        NavGithub = _loc.T("nav.github");
        NavSystemChip = _loc.T("nav.system");
        FirstRunTitle = _loc.T("dash.first.run");
        FirstRunHint = _loc.T("dash.first.run.hint");
        FirstRunCta = _loc.T("dash.first.run.cta");
    }

    /// <summary>Запуск WMI/polling-мониторинга по списку exe из всех профилей.</summary>
    public void StartGameDetectionIfEnabled()
    {
        if (!_settingsService.Current.GameAutoSwitchEnabled)
        {
            _gameDetection.Stop();
            return;
        }

        var allExe = _settingsService.Current.Profiles
            .SelectMany(p => p.GameExecutables)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var r = _gameDetection.Start(allExe);
        Log.Information("GameDetection start: {Msg}", r.Message);
    }

    private void OnGameStarted(object? sender, string exeName)
    {
        if (!_settingsService.Current.GameAutoSwitchEnabled) return;

        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // Найти профиль, в котором указан этот exe (предпочтение Gaming, иначе первый)
                var match = _settingsService.Current.Profiles
                    .Where(p => p.GameExecutables.Any(e =>
                        string.Equals(System.IO.Path.GetFileName(e), exeName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(System.IO.Path.GetFileNameWithoutExtension(e), System.IO.Path.GetFileNameWithoutExtension(exeName), StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(p => p.Kind == ProfileKind.Gaming)
                    .FirstOrDefault()
                    ?? OptimizationProfile.CreatePreset(ProfileKind.Gaming);

                if (_activeGameCount == 0)
                    _profileBeforeGame = _settingsService.Current.ActiveProfileId;

                _activeGameCount++;
                Log.Information("Игра запущена: {Exe} → профиль «{Profile}»", exeName, match.Name);

                var result = await _profileService.ApplyAsync(match);
                Dashboard.StatusMessage = $"Авто: {exeName} → {match.Name}. {result.Message}";
                Dashboard.LoadFromActiveProfile();
                Dashboard.StatusLine = result.Success
                    ? $"Игра {exeName}: профиль «{match.Name}»"
                    : result.Message;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GameStarted handler failed for {Exe}", exeName);
            }
        });
    }

    private void OnGameStopped(object? sender, string exeName)
    {
        if (!_settingsService.Current.GameAutoSwitchEnabled) return;

        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                _activeGameCount = Math.Max(0, _activeGameCount - 1);
                if (_activeGameCount > 0) return;

                Log.Information("Последняя отслеживаемая игра закрыта: {Exe}", exeName);

                // Вернуть профиль «до игры» или Default / Office
                OptimizationProfile? restore = null;
                if (_profileBeforeGame is Guid id)
                    restore = _settingsService.Current.Profiles.FirstOrDefault(p => p.Id == id);

                restore ??= _settingsService.Current.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Default)
                            ?? OptimizationProfile.CreatePreset(ProfileKind.Default);

                if (restore.Kind == ProfileKind.Default || (!restore.EnableTimer && !restore.EnablePowerScheme))
                {
                    var revert = await _profileService.RevertAsync();
                    Dashboard.StatusMessage = $"Игра закрыта ({exeName}). {revert.Message}";
                }
                else
                {
                    var apply = await _profileService.ApplyAsync(restore);
                    Dashboard.StatusMessage = $"Игра закрыта ({exeName}). {apply.Message}";
                }

                _profileBeforeGame = null;
                Dashboard.LoadFromActiveProfile();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GameStopped handler failed for {Exe}", exeName);
            }
        });
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentNavKey = page;
        switch (page)
        {
            case "Dashboard":
                CurrentPage = Dashboard;
                CurrentPageTitle = _loc.T("page.dashboard.title");
                CurrentPageSubtitle = _loc.T("page.dashboard.subtitle");
                break;
            case "Profiles":
                Profiles.Reload();
                CurrentPage = Profiles;
                CurrentPageTitle = _loc.T("page.profiles.title");
                CurrentPageSubtitle = _loc.T("page.profiles.subtitle");
                break;
            case "Monitoring":
                CurrentPage = Monitoring;
                CurrentPageTitle = _loc.T("page.monitoring.title");
                CurrentPageSubtitle = _loc.T("page.monitoring.subtitle");
                break;
            case "Plugins":
                Plugins.Reload();
                CurrentPage = Plugins;
                CurrentPageTitle = _loc.T("page.plugins.title");
                CurrentPageSubtitle = _loc.T("page.plugins.subtitle");
                break;
            case "Backups":
                Backups.Reload();
                CurrentPage = Backups;
                CurrentPageTitle = _loc.T("page.backups.title");
                CurrentPageSubtitle = _loc.T("page.backups.subtitle");
                break;
            case "Tips":
                CurrentPage = Tips;
                CurrentPageTitle = _loc.T("page.tips.title");
                CurrentPageSubtitle = _loc.T("page.tips.subtitle");
                break;
            case "Settings":
                CurrentPage = Settings;
                CurrentPageTitle = _loc.T("page.settings.title");
                CurrentPageSubtitle = _loc.T("page.settings.subtitle");
                break;
        }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/swd3k/antilag-next",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Open GitHub failed");
        }
    }

    [RelayCommand]
    private async Task RunFirstBenchmarkAsync()
    {
        await Dashboard.RunBenchmarkCommand.ExecuteAsync(null);
        ShowFirstRunBanner = !_settingsService.Current.FirstRunCompleted;
    }
}
