using System.Collections.ObjectModel;
using System.Windows;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AntiLagNext.App.ViewModels;

/// <summary>История JSON-бэкапов: просмотр, точечный откат, удаление.</summary>
public partial class BackupsViewModel : ViewModelBase
{
    private readonly IBackupService _backup;
    private readonly ITimerManager _timer;

    public ObservableCollection<BackupRecord> Backups { get; } = new();

    [ObservableProperty]
    private BackupRecord? _selectedBackup;

    [ObservableProperty]
    private string _detailText = string.Empty;

    public BackupsViewModel(IBackupService backup, ITimerManager timer)
    {
        _backup = backup;
        _timer = timer;
        Reload();
    }

    public void Reload()
    {
        Backups.Clear();
        foreach (var b in _backup.LoadAll())
            Backups.Add(b);
        SelectedBackup = Backups.FirstOrDefault();
        StatusMessage = Backups.Count == 0
            ? "Бэкапов пока нет. Они появятся после «Применить оптимизации»."
            : $"Записей: {Backups.Count} · {_backup.BackupDirectory}";
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
        StatusMessage = "Восстановление…";
        try
        {
            _timer.Release();
            var r = await _backup.RestoreAsync(SelectedBackup);
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
