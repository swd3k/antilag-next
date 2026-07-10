using System.Collections.ObjectModel;
using System.Windows;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Localization;
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
    private readonly ILocalizationService _loc;

    public ObservableCollection<OptimizationProfile> Profiles { get; } = new();

    [ObservableProperty] private OptimizationProfile? _selectedProfile;
    [ObservableProperty] private string _newGameExe = string.Empty;
    [ObservableProperty] private string _newExclusion = string.Empty;
    [ObservableProperty] private bool _canDeleteSelected;
    [ObservableProperty] private string _deleteTooltip = "";

    [ObservableProperty] private string _labelListTitle = "";
    [ObservableProperty] private string _labelListSub = "";
    [ObservableProperty] private string _labelSave = "";
    [ObservableProperty] private string _labelNew = "";
    [ObservableProperty] private string _labelDelete = "";
    [ObservableProperty] private string _labelGameExes = "";
    [ObservableProperty] private string _labelAdd = "";
    [ObservableProperty] private string _labelApply = "";

    public ProfilesViewModel(
        ISettingsService settings,
        IProfileService profiles,
        IGameDetectionService gameDetection,
        ILocalizationService loc)
    {
        _settings = settings;
        _profiles = profiles;
        _gameDetection = gameDetection;
        _loc = loc;
        _loc.CultureChanged += (_, _) => RefreshLocalization();
        RefreshLocalization();
        Reload();
    }

    public void RefreshLocalization()
    {
        LabelListTitle = _loc.T("profiles.list.title");
        LabelListSub = _loc.T("profiles.list.sub");
        LabelSave = _loc.T("profiles.save");
        LabelNew = _loc.T("profiles.new");
        LabelDelete = _loc.T("profiles.delete");
        LabelGameExes = _loc.T("profiles.game.exes");
        LabelAdd = _loc.T("profiles.add");
        LabelApply = _loc.T("profiles.apply");
        UpdateCanDelete();
    }

    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in _settings.Current.Profiles)
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.Current.ActiveProfileId)
                          ?? Profiles.FirstOrDefault();
        UpdateCanDelete();
    }

    partial void OnSelectedProfileChanged(OptimizationProfile? value) => UpdateCanDelete();

    private void UpdateCanDelete()
    {
        CanDeleteSelected = IsCustom(SelectedProfile);
        DeleteTooltip = CanDeleteSelected
            ? _loc.T("profiles.delete")
            : _loc.T("profiles.delete.only.custom");
    }

    private static bool IsCustom(OptimizationProfile? p)
    {
        if (p is null) return false;
        if (p.Kind == ProfileKind.Custom) return true;
        // Fallback: legacy rows without Kind=Custom but custom names
        if (p.Kind is ProfileKind.Default or ProfileKind.Gaming or ProfileKind.Office)
            return false;
        return true;
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
        StatusMessage = r.Success ? _loc.T("profiles.saved") : r.Message;
        RestartGameDetection();
    }

    [RelayCommand]
    private void AddCustomProfile()
    {
        int n = Profiles.Count(p => IsCustom(p)) + 1;
        var p = new OptimizationProfile
        {
            Name = string.Format(_loc.T("profiles.custom.name"), n),
            Kind = ProfileKind.Custom,
            Description = _loc.T("profiles.custom.desc"),
            EnableTimer = true,
            TimerTargetMs = 0.5,
            EnablePowerScheme = true,
            EnableCoreParkingControl = true,
            CoreParkingMode = CoreParkingMode.AllActive
        };
        Profiles.Add(p);
        SelectedProfile = p;
        SaveProfiles();
        UpdateCanDelete();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = _loc.T("profiles.delete.only.custom");
            return;
        }
        DeleteProfileCore(SelectedProfile);
    }

    [RelayCommand]
    private void DeleteProfile(OptimizationProfile? profile)
    {
        if (profile is null) return;
        // Select then delete so UI stays consistent
        SelectedProfile = profile;
        DeleteProfileCore(profile);
    }

    private void DeleteProfileCore(OptimizationProfile profile)
    {
        if (!IsCustom(profile))
        {
            StatusMessage = _loc.T("profiles.delete.only.custom");
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(_loc.T("profiles.delete.confirm"), profile.Name),
            _loc.T("app.name"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        bool wasActive = _settings.Current.ActiveProfileId == profile.Id;
        Profiles.Remove(profile);
        _settings.Current.Profiles = Profiles.ToList();

        if (wasActive)
        {
            var fallback = Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Default)
                           ?? Profiles.FirstOrDefault();
            if (fallback != null)
                _settings.Current.ActiveProfileId = fallback.Id;
        }

        var r = _settings.Save();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.Current.ActiveProfileId)
                          ?? Profiles.FirstOrDefault();
        UpdateCanDelete();
        StatusMessage = r.Success
            ? string.Format(_loc.T("profiles.delete.done"), profile.Name)
            : r.Message;
        RestartGameDetection();
        Log.Information("Custom profile deleted: {Name}", profile.Name);
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
