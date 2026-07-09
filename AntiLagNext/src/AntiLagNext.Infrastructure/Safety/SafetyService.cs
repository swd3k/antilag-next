using System.Diagnostics;
using System.Runtime.InteropServices;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Native;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Safety;

/// <summary>
/// Реализация <see cref="ISafetyService"/>: единая точка безопасности перед любыми изменениями.
///
/// Поток работы:
/// 1. BeforeChangesAsync: создаёт точку восстановления (если включено в настройках и служба
///    System Restore активна), открывает сессию бэкапа. Возвращает GUID сессии.
/// 2. Сервисы оптимизации (Timer/Power/CoreParking/...) регистрируют в сессии снимки значений
///    через IBackupService, затем изменяют систему.
/// 3. CommitChanges: сохраняет сессию на диск.
/// 4. ResetAllAsync: загружает последний бэкап и восстанавливает все значения, отпускает таймер,
///    закрывает точку восстановления.
/// </summary>
public sealed class SafetyService : ISafetyService
{
    private readonly IBackupService _backup;
    private readonly ITimerManager _timer;
    private readonly IPowerManager _power;
    private readonly AppSettings _settings;
    private DateTime _lastRestorePointUtc = DateTime.MinValue;

    public SafetyService(IBackupService backup, ITimerManager timer, IPowerManager power, AppSettings settings)
    {
        _backup = backup;
        _timer = timer;
        _power = power;
        _settings = settings;
    }

    public async Task<OperationResult<Guid>> BeforeChangesAsync(string operationName, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Точка восстановления (если включено)
            bool rpCreated = false;
            string? rpError = null;
            if (_settings.CreateRestorePoint)
            {
                var rp = TryCreateRestorePoint(operationName);
                rpCreated = rp.success;
                rpError = rp.error;
            }

            // 2. Сессия бэкапа
            var activeScheme = _power.GetActiveScheme();
            string? schemeBefore = activeScheme.Success ? activeScheme.Value!.ToString() : null;
            var sessionId = _backup.BeginSession(operationName, schemeBefore);
            _backup.SetRestorePointStatus(sessionId, rpCreated, rpError);

            await Task.CompletedTask;
            return OperationResult<Guid>.Ok(sessionId, "Защита перед изменениями подготовлена.");
        }
        catch (Exception ex)
        {
            return OperationResult<Guid>.Fail("Не удалось подготовить защиту.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult CommitChanges(Guid sessionId)
    {
        var result = _backup.CommitSession(sessionId);
        return result.Success
            ? OperationResult.Ok($"Изменения зафиксированы. {result.Message}")
            : OperationResult.Fail("Не удалось зафиксировать бэкап.", detail: result.Detail);
    }

    public async Task<OperationResult> ResetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var errors = new List<string>();

            // 1. Отпустить таймер
            try
            {
                var rel = _timer.Release();
                if (!rel.Success) errors.Add($"Таймер: {rel.Message}");
            }
            catch (Exception ex) { errors.Add($"Таймер: {ex.Message}"); }

            // 2. Восстановить значения из последнего бэкапа
            var latest = _backup.LoadLatest();
            if (latest.Success && latest.Value != null)
            {
                var restore = await _backup.RestoreAsync(latest.Value, cancellationToken);
                if (!restore.Success) errors.Add(restore.Message);
            }

            // 3. Если бэкапа нет — явно переключить на Balanced (минимально-безопасный откат)
            else
            {
                var setActive = _power.SetActiveScheme(PowerGuids.SchemeBalanced);
                if (!setActive.Success) errors.Add(setActive.Message);
            }

            // 4. Завершить точку восстановления (END_SYSTEM_CHANGE)
            TryEndRestorePoint();

            return errors.Count == 0
                ? OperationResult.Ok("Все оптимизации сброшены, система возвращена к исходному состоянию.")
                : OperationResult.Fail("Сброс выполнен частично.", detail: string.Join("; ", errors));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Сбой при сбросе оптимизаций.", detail: ex.Message, ex: ex);
        }
    }

    /// <summary>
    /// Создать точку восстановления (BEGIN_SYSTEM_CHANGE).
    /// Учитывает квоту SystemRestorePointCreationFrequency (по умолчанию 24 ч на Win10 1809+).
    /// </summary>
    private (bool success, string? error) TryCreateRestorePoint(string description)
    {
        // Квота: между точками должно пройти достаточно времени (читаем из реестра, дефолт 86400 с = 24 ч,
        // но многие системы имеют 0/10с — мы проверяем минимально 10 с между нашими точками).
        if ((DateTime.UtcNow - _lastRestorePointUtc).TotalSeconds < 10)
        {
            return (false, "Слишком частое создание точки (квота). Точка пропущена, бэкап всё равно создан.");
        }

        if (!IsSystemRestoreEnabled())
        {
            return (false, "Восстановление системы отключено. Включите службу в rstrui.exe для создания точек.");
        }

        try
        {
            var info = new SrClient.RESTOREPOINTINFO
            {
                dwEventType = SrClient.BeginSystemChange,
                dwRestorePtType = SrClient.ModifySettings,
                llSequenceNumber = 0,
                szDescription = TruncateDescription(description)
            };

            if (!SrClient.SRSetRestorePointW(ref info, out var status))
            {
                int err = Marshal.GetLastWin32Error();
                _lastRestorePointUtc = DateTime.UtcNow;
                return (false, $"SRSetRestorePointW: {Win32Result.FormatMessage(err)} (status {status.nStatus})");
            }

            _lastRestorePointUtc = DateTime.UtcNow;
            // sequence number сохраняется внутри SrClient; для END_SYSTEM_CHANGE используется
            // тот же llSequenceNumber, что вернулся в status. Мы храним его статически.
            _lastSequenceNumber = status.llSequenceNumber;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private long _lastSequenceNumber;

    /// <summary>Завершить точку восстановления (END_SYSTEM_CHANGE) — необязательная, но корректная операция.</summary>
    private void TryEndRestorePoint()
    {
        if (_lastSequenceNumber == 0) return;
        try
        {
            var info = new SrClient.RESTOREPOINTINFO
            {
                dwEventType = SrClient.EndSystemChange,
                dwRestorePtType = SrClient.ModifySettings,
                llSequenceNumber = _lastSequenceNumber,
                szDescription = string.Empty
            };
            SrClient.SRSetRestorePointW(ref info, out _);
            _lastSequenceNumber = 0;
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Проверить, включено ли восстановление системы (читаем флаг в реестре).
    /// </summary>
    private static bool IsSystemRestoreEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: false);
            if (key?.GetValue("RPSessionInterval") is int sessionInterval)
                return sessionInterval >= 0; // 0 = включено, но квота; ключ может отсутствовать = включено по умолчанию
            // Дополнительная проверка: настройка DisableConfig
            using var cfg = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", writable: false);
            if (cfg?.GetValue("DisableConfig") is int disabled)
                return disabled == 0;
            return true; // По умолчанию считаем включённым; SRSetRestorePoint скажет точно
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Описание точки восстановления ограничено 256 символами Windows.</summary>
    private static string TruncateDescription(string description)
        => description.Length <= 256 ? description : description[..256];
}
