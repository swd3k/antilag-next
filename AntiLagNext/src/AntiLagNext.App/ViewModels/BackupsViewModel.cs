using System.Collections.ObjectModel;
using System.Windows;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Localization;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>История JSON-бэкапов: просмотр, точечный откат, удаление.</summary>
public partial class BackupsViewModel : ViewModelBase
{
    private readonly IBackupService _backup;
    private readonly ITimerManager _timer;
    private readonly ILocalizationService _loc;
    private readonly SystemMutationGate _mutationGate;

    public ObservableCollection<BackupRecord> Backups { get; } = new();

    [ObservableProperty]
    private BackupRecord? _selectedBackup;

    [ObservableProperty]
    private string _detailText = string.Empty;

    [ObservableProperty] private string _labelRefresh = "REFRESH";
    [ObservableProperty] private string _labelRestore = "RESTORE";
    [ObservableProperty] private string _labelDelete = "DELETE";
    [ObservableProperty] private string _labelOpenFolder = "OPEN FOLDER";
    [ObservableProperty] private string _labelDetail = "SNAPSHOT DETAIL";
    [ObservableProperty] private string _labelEmpty = "No backups yet.";

    public BackupsViewModel(
        IBackupService backup,
        ITimerManager timer,
        ILocalizationService loc,
        SystemMutationGate mutationGate)
    {
        _backup = backup;
        _timer = timer;
        _loc = loc;
        _mutationGate = mutationGate;
        _loc.CultureChanged += (_, _) => RefreshLocalization();
        RefreshLocalization();
        Reload();
    }

    public void RefreshLocalization()
    {
        LabelRefresh = _loc.T("backups.refresh");
        LabelRestore = _loc.T("backups.restore");
        LabelDelete = _loc.T("backups.delete");
        LabelOpenFolder = _loc.T("backups.open.folder");
        LabelDetail = _loc.T("backups.detail");
        LabelEmpty = _loc.T("backups.empty");
        if (Backups.Count == 0)
            StatusMessage = LabelEmpty;
    }

    public void Reload()
    {
        Backups.Clear();
        foreach (var b in _backup.LoadAll())
            Backups.Add(b);
        SelectedBackup = Backups.FirstOrDefault();
        StatusMessage = Backups.Count == 0
            ? _loc.T("backups.empty")
            : $"{Backups.Count} · {_backup.BackupDirectory}";
    }

    partial void OnSelectedBackupChanged(BackupRecord? value)
    {
        if (value == null)
        {
            DetailText = string.Empty;
            return;
        }

        DetailText =
            $"Операция: {value.OperationName}\n" +
            $"Время (локальное): {value.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
            $"Схема до: {value.ActiveSchemeGuidBefore ?? "—"}\n" +
            $"Реестр: {value.RegistryEntries.Count} · Power: {value.PowerEntries.Count}\n" +
            $"System Restore: {(value.SystemRestorePointCreated ? "создана" : "нет")}" +
            (string.IsNullOrEmpty(value.SystemRestorePointError) ? "" : $" ({value.SystemRestorePointError})") + "\n" +
            $"Файл: {value.SourceFilePath ?? "—"}";
    }

    [RelayCommand]
    private void Refresh() => Reload();

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        if (SelectedBackup == null || IsBusy) return;

        var confirm = MessageBox.Show(
            $"Восстановить состояние из бэкапа?\n\n{SelectedBackup.DisplaySummary}\n\n" +
            "Таймер будет отпущен. Схема питания и ключи реестра — из снимка.",
            "AntiLag Next — откат",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusMessage = _loc.T("status.busy");
        try
        {
            var selected = SelectedBackup;
            var r = await _mutationGate.RunAsync(async () =>
            {
                _timer.Release();
                return await _backup.RestoreAsync(selected!);
            });
            StatusMessage = r.Message + (r.Detail is { Length: > 0 } d ? " · " + d : "");
            Log.Information("Restore backup: {Msg}", StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка: " + ex.Message;
            Log.Error(ex, "Restore backup failed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedBackup == null || IsBusy) return;

        var confirm = MessageBox.Show(
            $"Удалить файл бэкапа?\n\n{SelectedBackup.DisplaySummary}",
            "AntiLag Next",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var r = _backup.Delete(SelectedBackup);
        StatusMessage = r.Message;
        Reload();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_backup.BackupDirectory);
            ProcessStart(_backup.BackupDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private static void ProcessStart(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
