using AntiLagNext.Core.Models;

namespace AntiLagNext.Core.Plugins;

/// <summary>Категория плагина для группировки в UI.</summary>
public enum PluginCategory
{
    Core = 0,
    Power = 1,
    Gpu = 2,
    Network = 3,
    Input = 4,
    Game = 5,
    Experimental = 6
}

/// <summary>Оценка влияния на latency (честная шкала для tooltip).</summary>
public enum LatencyImpact
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Experimental = 4
}

/// <summary>Тип UI-поля, которое плагин может добавить на страницу Plugins.</summary>
public enum PluginSettingKind
{
    Toggle = 0,
    Integer = 1,
    Text = 2
}

/// <summary>Описание настройки плагина (без ссылок на WPF).</summary>
public sealed class PluginUiDescriptor
{
    public required string Key { get; init; }
    public required string LabelKey { get; init; }
    public string? TooltipKey { get; init; }
    public PluginSettingKind Kind { get; init; } = PluginSettingKind.Toggle;
    public object? DefaultValue { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
}

/// <summary>Контекст применения: профиль + сессия бэкапа.</summary>
public sealed class PluginApplyContext
{
    public required OptimizationProfile Profile { get; init; }
    public Guid BackupSessionId { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public bool IsReCalibrate { get; init; }
}

/// <summary>
/// Ограниченный API для плагинов (без полного DI).
/// Расширения не должны тянуть WPF.
/// </summary>
public interface IPluginServices
{
    string PluginsDirectory { get; }
    string AppDataRoot { get; }
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? ex = null);
}

/// <summary>Состояние плагина для UI/CLI.</summary>
public enum PluginRuntimeState
{
    Idle = 0,
    Applied = 1,
    Partial = 2,
    Error = 3,
    Unsupported = 4
}

/// <summary>Статус плагина (TZ: GetStatus).</summary>
public sealed class PluginStatus
{
    public PluginRuntimeState State { get; init; } = PluginRuntimeState.Idle;
    public string Message { get; init; } = string.Empty;
    public DateTime? LastChangedUtc { get; init; }
}

/// <summary>Контракт подключаемого модуля оптимизации (IPlugin из ТЗ).</summary>
public interface IAntiLagPlugin : IDisposable
{
    string Id { get; }
    /// <summary>i18n key for display name.</summary>
    string NameKey { get; }
    string DescriptionKey { get; }
    string Version { get; }
    PluginCategory Category { get; }
    LatencyImpact Impact { get; }
    bool IsBuiltIn { get; }

    /// <summary>Порядок применения (меньше = раньше). Experimental — в конце.</summary>
    int ApplyOrder { get; }

    /// <summary>Пользовательский enable (хранится в settings).</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// true = apply идёт через ProfileService (ядро); плагин только документируется в каталоге.
    /// false = ApplyAsync вызывается host'ом дополнительно.
    /// </summary>
    bool AppliedByCore { get; }

    /// <summary>false для experimental без поддержки ОС/железа (TZ: IsSupported).</summary>
    bool IsSupported(out string? reason);

    PluginStatus GetStatus();

    Task InitializeAsync(IPluginServices services, CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyAsync(PluginApplyContext context);

    Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<PluginUiDescriptor> GetUiDescriptors();

    /// <summary>Текущие значения UI-настроек (key → value).</summary>
    IReadOnlyDictionary<string, object?> GetSettingValues();

    void SetSettingValue(string key, object? value);
}

/// <summary>Каталог + lifecycle плагинов.</summary>
public interface IPluginCatalog
{
    IReadOnlyList<IAntiLagPlugin> Plugins { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <param name="appliedPluginIds">When non-null, successfully applied extension plugin ids are appended.</param>
    Task<OperationResult> ApplyEnabledExtensionsAsync(
        PluginApplyContext context,
        ICollection<string>? appliedPluginIds = null);

    Task<OperationResult> RevertAllExtensionsAsync(CancellationToken cancellationToken = default);

    IAntiLagPlugin? GetById(string id);
}
