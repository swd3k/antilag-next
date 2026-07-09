using System.Windows;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
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
    private int _activeGameCount;
    private Guid? _profileBeforeGame;

    public DashboardViewModel Dashboard { get; }
    public ProfilesViewModel Profiles { get; }
    public MonitoringViewModel Monitoring { get; }
    public BackupsViewModel Backups { get; }
    public TipsViewModel Tips { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _currentPageTitle = "Панель оптимизаций";

    [ObservableProperty]
    private bool _showFirstRunBanner;

    public MainViewModel(
        DashboardViewModel dashboard,
        ProfilesViewModel profiles,
        MonitoringViewModel monitoring,
        BackupsViewModel backups,
        TipsViewModel tips,
        SettingsViewModel settings,
        ISettingsService settingsService,
        IProfileService profileService,
        IGameDetectionService gameDetection)
    {
        Dashboard = dashboard;
        Profiles = profiles;
        Monitoring = monitoring;
        Backups = backups;
        Tips = tips;
        Settings = settings;
        _settingsService = settingsService;
        _profileService = profileService;
        _gameDetection = gameDetection;
        _currentPage = dashboard;

        ShowFirstRunBanner = !settingsService.Current.FirstRunCompleted;
        SettingsViewModel.ApplyTheme(settings.Theme);

        _gameDetection.GameStarted += OnGameStarted;
        _gameDetection.GameStopped += OnGameStopped;
        StartGameDetectionIfEnabled();
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
        switch (page)
        {
            case "Dashboard":
                CurrentPage = Dashboard;
                CurrentPageTitle = "Панель оптимизаций";
                break;
            case "Profiles":
                Profiles.Reload();
                CurrentPage = Profiles;
                CurrentPageTitle = "Профили";
                break;
            case "Monitoring":
                CurrentPage = Monitoring;
                CurrentPageTitle = "Мониторинг";
                break;
            case "Backups":
                Backups.Reload();
                CurrentPage = Backups;
                CurrentPageTitle = "История бэкапов";
                break;
            case "Tips":
                CurrentPage = Tips;
                CurrentPageTitle = "Подсказки";
                break;
            case "Settings":
                CurrentPage = Settings;
                CurrentPageTitle = "Настройки";
                break;
        }
    }

    [RelayCommand]
    private async Task RunFirstBenchmarkAsync()
    {
        await Dashboard.RunBenchmarkCommand.ExecuteAsync(null);
        ShowFirstRunBanner = !_settingsService.Current.FirstRunCompleted;
    }
}
