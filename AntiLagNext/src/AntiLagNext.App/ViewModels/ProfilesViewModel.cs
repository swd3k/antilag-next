using System.Collections.ObjectModel;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>Управление профилями: игровой / офисный / по умолчанию / пользовательские.</summary>
public partial class ProfilesViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly IGameDetectionService _gameDetection;

    public ObservableCollection<OptimizationProfile> Profiles { get; } = new();

    [ObservableProperty]
    private OptimizationProfile? _selectedProfile;

    [ObservableProperty]
    private string _newGameExe = string.Empty;

    [ObservableProperty]
    private string _newExclusion = string.Empty;

    public ProfilesViewModel(
        ISettingsService settings,
        IProfileService profiles,
        IGameDetectionService gameDetection)
    {
        _settings = settings;
        _profiles = profiles;
        _gameDetection = gameDetection;
        Reload();
    }

    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in _settings.Current.Profiles)
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.Current.ActiveProfileId)
                          ?? Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        if (SelectedProfile == null || IsBusy) return;
        IsBusy = true;
        try
        {
            _settings.Current.ActiveProfileId = SelectedProfile.Id;
            _settings.Save();
            var r = await _profiles.ApplyAsync(SelectedProfile);
            StatusMessage = r.Message;
            RestartGameDetection();
            Log.Information("Profile apply: {Name} -> {Msg}", SelectedProfile.Name, r.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "Profile apply failed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void SaveProfiles()
    {
        _settings.Current.Profiles = Profiles.ToList();
        var r = _settings.Save();
        StatusMessage = r.Message;
        RestartGameDetection();
    }

    [RelayCommand]
    private void AddCustomProfile()
    {
        var p = new OptimizationProfile
        {
            Name = $"Пользовательский {Profiles.Count + 1}",
            Kind = Core.Enums.ProfileKind.Custom,
            Description = "Скопируйте настройки с Dashboard или отредактируйте вручную.",
            EnableTimer = true,
            TimerTargetMs = 0.5,
            EnablePowerScheme = true
        };
        Profiles.Add(p);
        SelectedProfile = p;
        SaveProfiles();
    }

    [RelayCommand]
    private void AddGameExe()
    {
        if (SelectedProfile == null || string.IsNullOrWhiteSpace(NewGameExe)) return;
        var name = System.IO.Path.GetFileName(NewGameExe.Trim());
        if (!SelectedProfile.GameExecutables.Contains(name, StringComparer.OrdinalIgnoreCase))
            SelectedProfile.GameExecutables.Add(name);
        NewGameExe = string.Empty;
        OnPropertyChanged(nameof(SelectedProfile));
        SaveProfiles();
    }

    [RelayCommand]
    private void AddExclusion()
    {
        if (SelectedProfile == null || string.IsNullOrWhiteSpace(NewExclusion)) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(NewExclusion.Trim());
        if (!SelectedProfile.MemoryCleanupExclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
            SelectedProfile.MemoryCleanupExclusions.Add(name);
        NewExclusion = string.Empty;
        SaveProfiles();
    }

    private void RestartGameDetection()
    {
        if (!_settings.Current.GameAutoSwitchEnabled) return;
        var allExe = _settings.Current.Profiles.SelectMany(p => p.GameExecutables).Distinct().ToList();
        var r = _gameDetection.Start(allExe);
        Log.Information("GameDetection: {Msg}", r.Message);
    }
}
