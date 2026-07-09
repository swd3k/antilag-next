using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;

namespace AntiLagNext.Core.Abstractions;

// ============================================================================
//  АБСТРАКЦИИ ДОМЕННОГО СЛОЯ (AntiLagNext.Core.Abstractions)
//  Каждый интерфейс — отдельная возможность оптимизации/мониторинга.
//  Реализации живут в AntiLagNext.Infrastructure. Core зависит только от интерфейсов.
// ============================================================================

/// <summary>
/// Управление разрешением системного таймера через NtSetTimerResolution.
/// Автоподбор минимального стабильного разрешения вместо фиксированного значения.
/// </summary>
public interface ITimerManager
{
    /// <summary>Текущее состояние таймера.</summary>
    TimerState CurrentState { get; }

    /// <summary>Событие изменения состояния (для UI).</summary>
    event EventHandler<TimerState>? StateChanged;

    /// <summary>Получить доступные границы разрешения.</summary>
    TimerCaps GetCaps();

    /// <summary>
    /// Подобрать и установить минимальное стабильное разрешение около целевого значения.
    /// Проводит цикл измерения джиттера через QueryPerformanceCounter.
    /// </summary>
    /// <param name="targetMs">Желаемое разрешение, мс (0.5, 1.0 и т.д.).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<OperationResult<TimerState>> TuneAsync(double targetMs, CancellationToken cancellationToken = default);

    /// <summary>Отпустить таймер (Set=false). Система вернётся к разрешению по умолчанию.</summary>
    OperationResult Release();
}

/// <summary>
/// Управление схемой электропитания и её настройками через Win32 Power API (powrprof.dll).
/// Без шелла powercfg — только нативные вызовы.
/// </summary>
public interface IPowerManager
{
    /// <summary>Получить активную схему электропитания.</summary>
    OperationResult<Guid> GetActiveScheme();

    /// <summary>Активировать схему по GUID.</summary>
    OperationResult SetActiveScheme(Guid schemeGuid);

    /// <summary>Прочитать значение настройки (AC или DC).</summary>
    OperationResult<uint> ReadValue(Guid schemeGuid, Guid subGroup, Guid setting, bool isAc);

    /// <summary>Записать значение настройки (AC и/или DC) и применить.</summary>
    OperationResult WriteValue(Guid schemeGuid, Guid subGroup, Guid setting, uint acValue, uint dcValue);

    /// <summary>Снять атрибут "скрытая" с настройки (эквивалент powercfg -attributes ... -ATTRIB_HIDE).</summary>
    OperationResult UnhideSetting(Guid subGroup, Guid setting);

    /// <summary>Текущий источник питания (AC/DC).</summary>
    PowerSource GetCurrentPowerSource();
}

/// <summary>
/// Управление парковкой ядер с учётом гетерогенной архитектуры (P-cores/E-cores).
/// </summary>
public interface ICoreParkingManager
{
    /// <summary>Определить топологию процессора (количество P/E-cores).</summary>
    OperationResult<HybridCpuTopology> DetectTopology();

    /// <summary>Применить режим парковки в активной схеме.</summary>
    OperationResult ApplyMode(Guid schemeGuid, CoreParkingMode mode);
}

/// <summary>
/// Твики Game Mode / Game Bar / Game DVR / HAGS через реестр.
/// </summary>
public interface IGameModeManager
{
    /// <summary>Включить/выключить Game Mode и Game DVR.</summary>
    OperationResult SetGameMode(bool enabled, bool disableGameDvr);

    /// <summary>Включить/выключить HAGS (Hardware-accelerated GPU Scheduling).</summary>
    OperationResult SetHags(bool enabled);
}

/// <summary>
/// Очистка рабочего набора памяти (Empty Working Set) фоновых процессов.
/// </summary>
public interface IMemoryManager
{
    /// <summary>Очистить рабочий набор всех процессов, кроме списка исключений.</summary>
    /// <param name="exclusions">Имена процессов (без пути, без регистра), которые не трогать.</param>
    /// <returns>Количество очищенных процессов и освобождённый объём.</returns>
    OperationResult<MemoryCleanupStats> EmptyWorkingSets(IReadOnlyCollection<string> exclusions);
}

/// <summary>
/// Статистика очистки памяти.
/// </summary>
public sealed class MemoryCleanupStats
{
    public int ProcessesTrimmed { get; init; }
    public long BytesFreed { get; init; }
    public int ProcessesSkipped { get; init; }
}

/// <summary>
/// Сервис безопасности: точка восстановления и координация отката.
/// Вызывается ВСЕГДА перед любым изменением системы.
/// </summary>
public interface ISafetyService
{
    /// <summary>
    /// Вызвать ПЕРЕД любым изменением системы: создаёт точку восстановления (если включено)
    /// и открывает новую запись бэкапа для накопления снимков изменяемых значений.
    /// Возвращает идентификатор сессии бэкапа (для связывания с изменениями).
    /// </summary>
    Task<OperationResult<Guid>> BeforeChangesAsync(string operationName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Завершить сессию бэкапа: сохранить JSON-файл.
    /// </summary>
    OperationResult CommitChanges(Guid sessionId);

    /// <summary>
    /// Полный сброс всех оптимизаций: восстановить значения из последнего бэкапа,
    /// отпустить таймер, вернуть схему в Balanced, закрыть точку восстановления.
    /// </summary>
    Task<OperationResult> ResetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Сервис бэкапа: снимок значений реестра/power-plan и восстановление.
/// </summary>
public interface IBackupService
{
    /// <summary>Каталог хранения бэкапов.</summary>
    string BackupDirectory { get; }

    /// <summary>Начать новую сессию бэкапа (в памяти), вернуть идентификатор.</summary>
    Guid BeginSession(string operationName, string? activeSchemeGuidBefore);

    /// <summary>Добавить в сессию снимок значения реестра (вызывается до изменения значения).</summary>
    void SnapshotRegistryValue(Guid sessionId, RegistryBackupEntry entry);

    /// <summary>Добавить в сессию снимок значения power-plan.</summary>
    void SnapshotPowerValue(Guid sessionId, PowerBackupEntry entry);

    /// <summary>Зафиксировать статус точки восстановления в сессии.</summary>
    void SetRestorePointStatus(Guid sessionId, bool created, string? error);

    /// <summary>Сохранить сессию в JSON-файл на диск.</summary>
    OperationResult<BackupRecord> CommitSession(Guid sessionId);

    /// <summary>Загрузить самый свежий бэкап.</summary>
    OperationResult<BackupRecord> LoadLatest();

    /// <summary>Загрузить все бэкапы (для UI списка истории).</summary>
    IReadOnlyList<BackupRecord> LoadAll();

    /// <summary>Восстановить все значения из записи бэкапа.</summary>
    Task<OperationResult> RestoreAsync(BackupRecord record, CancellationToken cancellationToken = default);

    /// <summary>Удалить JSON-файл бэкапа с диска (если SourceFilePath задан).</summary>
    OperationResult Delete(BackupRecord record);
}

/// <summary>
/// Управление профилями оптимизации (сохранение/загрузка/применение).
/// </summary>
public interface IProfileService
{
    /// <summary>Активировать профиль: применить все включённые оптимизации с защитой бэкапа.</summary>
    Task<OperationResult> ApplyAsync(OptimizationProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Деактивировать профиль: откатить изменения (через SafetyService.ResetAll или точечный откат).</summary>
    Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Автоматическое обнаружение запуска/закрытия игр по списку exe (WMI Win32_ProcessStartTrace).
/// </summary>
public interface IGameDetectionService
{
    /// <summary>Событие запуска отслеживаемого процесса (имя exe).</summary>
    event EventHandler<string>? GameStarted;

    /// <summary>Событие закрытия отслеживаемого процесса.</summary>
    event EventHandler<string>? GameStopped;

    /// <summary>Запустить мониторинг WMI по списку отслеживаемых exe.</summary>
    OperationResult Start(IReadOnlyCollection<string> executableNames);

    /// <summary>Остановить мониторинг.</summary>
    void Stop();
}

/// <summary>
/// Мониторинг задержек системы в реальном времени.
/// </summary>
public interface IMonitoringService
{
    /// <summary>Событие нового отсчёта (сэмпл).</summary>
    event EventHandler<MonitoringSample>? SampleArrived;

    /// <summary>Текущий источник питания.</summary>
    PowerSource CurrentPowerSource { get; }

    /// <summary>Запустить мониторинг.</summary>
    void Start(TimeSpan interval);

    /// <summary>Остановить мониторинг.</summary>
    void Stop();
}

/// <summary>
/// Бенчмарк при первом запуске: замер DPC/scheduling-задержки, свип таймера, рекомендация.
/// </summary>
public interface IBenchmarkService
{
    /// <summary>Провести замер и сформировать рекомендацию (с пояснениями на русском).</summary>
    Task<OperationResult<BenchmarkResult>> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// GPU-оптимизации: Low Latency Mode (NVIDIA через реестр/NVAPI, AMD через реестр/ADLX).
/// Полный NVAPI/ADLX SDK не распространяется — используем публичные registry-пути драйверов
/// и опциональную native-DLL. Без установленных драйверов методы возвращают мягкий отказ.
/// </summary>
public interface IGpuManager
{
    /// <summary>Определить вендора GPU (NVIDIA / AMD / Intel / Unknown).</summary>
    string DetectVendor();

    /// <summary>
    /// Включить/выключить Low Latency Mode.
    /// NVIDIA: Ultra Low Latency (реестр NV_ControlPanel) / Reflex On+Boost при наличии.
    /// AMD: Anti-Lag / Anti-Lag+ через драйверный профиль.
    /// </summary>
    OperationResult SetLowLatencyMode(bool enabled);

    /// <summary>
    /// Ограничить размер flip-queue (pre-rendered frames). 1 = минимальная задержка.
    /// </summary>
    OperationResult SetMaxPreRenderedFrames(int frames);

    /// <summary>Прочитать текущее состояние (для UI).</summary>
    OperationResult<string> GetStatusSummary();
}

/// <summary>
/// Хранилище настроек приложения (load/save).
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    OperationResult Load();
    OperationResult Save();
}

// Нужен using для AppSettings — добавим в начале файла namespace Settings
