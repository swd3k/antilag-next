using System.Collections.ObjectModel;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Localization;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Infrastructure.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>Каталог плагинов + opt-in extensions + UI descriptors.</summary>
public partial class PluginsViewModel : ViewModelBase
{
    private readonly IPluginCatalog _catalog;
    private readonly IProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;

    public ObservableCollection<PluginRowViewModel> Items { get; } = new();

    [ObservableProperty] private string _headerTitle = "Plugins";
    [ObservableProperty] private string _headerSubtitle = "";
    [ObservableProperty] private string _labelSave = "SAVE FLAGS";
    [ObservableProperty] private string _labelApply = "APPLY PROFILE + PLUGINS";
    [ObservableProperty] private string _labelHelp = "";

    public PluginsViewModel(
        IPluginCatalog catalog,
        IProfileService profiles,
        ISettingsService settings,
        ILocalizationService loc)
    {
        _catalog = catalog;
        _profiles = profiles;
        _settings = settings;
        _loc = loc;
        _loc.CultureChanged += (_, _) => RefreshTexts();
        RefreshTexts();
        Reload();
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var p in _catalog.Plugins.OrderBy(x => x.Category).ThenBy(x => x.Id))
            Items.Add(new PluginRowViewModel(p, _loc));
    }

    private void RefreshTexts()
    {
        HeaderTitle = _loc.T("plugins.title");
        HeaderSubtitle = _loc.T("plugins.subtitle");
        LabelSave = _loc.T("plugins.save");
        LabelApply = _loc.T("plugins.apply");
        LabelHelp = _loc.T("plugins.help");
        foreach (var row in Items)
            row.RefreshLoc(_loc);
    }

    [RelayCommand]
    private void SaveFlags()
    {
        if (_catalog is PluginCatalog pc)
            pc.PersistEnabledFlags();
        else
        {
            foreach (var row in Items)
                _settings.Current.PluginEnabled[row.Id] = row.IsEnabled;
        }
        var r = _settings.Save();
        StatusMessage = r.Success ? _loc.T("plugins.save") + " ✓" : r.Message;
        Log.Information("Plugin flags saved");
    }

    [RelayCommand]
    private async Task ApplyExtensionsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "…";
        try
        {
            // Persist toggles into plugin objects
            foreach (var row in Items)
            {
                var p = _catalog.GetById(row.Id);
                if (p != null && !p.AppliedByCore)
                    p.IsEnabled = row.IsEnabled;
            }

            if (_catalog is PluginCatalog pc)
                pc.PersistEnabledFlags();
            _settings.Save();

            var profile = _settings.Current.GetActiveProfile();
            // Full profile apply (core + extensions)
            var result = await _profiles.ApplyAsync(profile);
            StatusMessage = result.Message;
            Log.Information("Plugins apply via profile: {Msg}", result.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "Plugin apply failed");
        }
        finally { IsBusy = false; }
    }
}

public partial class PluginRowViewModel : ObservableObject
{
    private readonly IAntiLagPlugin _plugin;

    public string Id => _plugin.Id;
    public bool AppliedByCore => _plugin.AppliedByCore;
    public bool IsBuiltIn => _plugin.IsBuiltIn;
    public string Version => _plugin.Version;
    public PluginCategory Category => _plugin.Category;
    public LatencyImpact Impact => _plugin.Impact;

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _impactLabel = "";
    [ObservableProperty] private string _kindLabel = "";
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _canToggle;

    public PluginRowViewModel(IAntiLagPlugin plugin, ILocalizationService loc)
    {
        _plugin = plugin;
        IsEnabled = plugin.IsEnabled;
        CanToggle = !plugin.AppliedByCore;
        RefreshLoc(loc);
    }

    public void RefreshLoc(ILocalizationService loc)
    {
        DisplayName = loc.T(_plugin.NameKey);
        Description = loc.T(_plugin.DescriptionKey);
        ImpactLabel = loc.T(_plugin.Impact switch
        {
            LatencyImpact.High => "impact.high",
            LatencyImpact.Medium => "impact.medium",
            LatencyImpact.Low => "impact.low",
            LatencyImpact.Experimental => "impact.experimental",
            _ => "impact.none"
        });
        KindLabel = _plugin.IsBuiltIn ? loc.T("plugins.builtin") : loc.T("plugins.external");
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (CanToggle)
            _plugin.IsEnabled = value;
    }
}
