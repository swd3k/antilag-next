using System.Collections.ObjectModel;
using AntiLagNext.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntiLagNext.App.ViewModels;

/// <summary>Справочник подсказок: что повышает отзывчивость и какие есть побочные эффекты.</summary>
public partial class TipsViewModel : ViewModelBase
{
    private readonly ILocalizationService _loc;

    public ObservableCollection<TipItem> Tips { get; } = new();

    [ObservableProperty] private string _labelBenefit = "Benefit";
    [ObservableProperty] private string _labelTradeoff = "Tradeoff";

    public TipsViewModel(ILocalizationService loc)
    {
        _loc = loc;
        _loc.CultureChanged += (_, _) => Reload();
        Reload();
    }

    public void Reload()
    {
        LabelBenefit = _loc.T("tips.benefit");
        LabelTradeoff = _loc.T("tips.tradeoff");
        Tips.Clear();
        Tips.Add(new TipItem(
            _loc.T("tip.timer.title"),
            _loc.T("tip.timer.benefit"),
            _loc.T("tip.timer.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.power.title"),
            _loc.T("tip.power.benefit"),
            _loc.T("tip.power.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.cores.title"),
            _loc.T("tip.cores.benefit"),
            _loc.T("tip.cores.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.gamemode.title"),
            _loc.T("tip.gamemode.benefit"),
            _loc.T("tip.gamemode.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.hags.title"),
            _loc.T("tip.hags.benefit"),
            _loc.T("tip.hags.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.gpu.title"),
            _loc.T("tip.gpu.benefit"),
            _loc.T("tip.gpu.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.mem.title"),
            _loc.T("tip.mem.benefit"),
            _loc.T("tip.mem.tradeoff")));
        Tips.Add(new TipItem(
            _loc.T("tip.backup.title"),
            _loc.T("tip.backup.benefit"),
            _loc.T("tip.backup.tradeoff")));
    }
}

public sealed class TipItem
{
    // set-аксессоры нужны: MaterialDesign/WPF может поднять Mode=TwoWay на Text,
    // а get-only свойства тогда валят XamlParseException при открытии Tips.
    public string Title { get; set; }
    public string Benefit { get; set; }
    public string Tradeoff { get; set; }

    public TipItem(string title, string benefit, string tradeoff)
    {
        Title = title;
        Benefit = benefit;
        Tradeoff = tradeoff;
    }
}
