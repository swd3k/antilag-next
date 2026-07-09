using CommunityToolkit.Mvvm.ComponentModel;

namespace AntiLagNext.App.ViewModels;

/// <summary>Базовый ViewModel с ObservableObject (CommunityToolkit.Mvvm).</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;
}
