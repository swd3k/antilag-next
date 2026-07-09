using AntiLagNext.Core.Enums;

namespace AntiLagNext.Core.Models;

/// <summary>
/// Один отсчёт (сэмпл) мониторинга задержек системы в реальном времени.
/// Все поля — в понятных единицах (мс, %, МБ).
/// </summary>
public sealed class MonitoringSample
{
    /// <summary>Время взятия отсчёта.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Измеренная scheduling/DPC-задержка в микросекундах.
    /// Получена высокоприоритетным зондом с waitable-timer: отклонение реального периода от запрошенного.
    /// Чем выше — тем больше система «захлёбывается» под DPC/ISR (драйверами).
    /// </summary>
    public double SchedulingLatencyUs { get; init; }

    /// <summary>Текущее разрешение системного таймера в миллисекундах (по NtQueryTimerResolution).</summary>
    public double TimerResolutionMs { get; init; }

    /// <summary>Время кадра по DXGI (фреймтайм) в миллисекундах; null, если нет активного Present-сессии.</summary>
    public double? FrameTimeMs { get; init; }

    /// <summary>Загрузка CPU в процентах (0–100).</summary>
    public float CpuUsagePercent { get; init; }

    /// <summary>Используемая оперативная память в МБ.</summary>
    public float UsedMemoryMb { get; init; }

    /// <summary>Источник питания (AC/DC) на момент замера.</summary>
    public PowerSource PowerSource { get; init; }
}

/// <summary>
/// Результат бенчмарка при первом запуске: метрики и рекомендация.
/// </summary>
public sealed class BenchmarkResult
{
    /// <summary>Максимальная scheduling-задержка за замер, мкс.</summary>
    public double MaxSchedulingLatencyUs { get; init; }

    /// <summary>99-й перцентиль scheduling-задержки, мкс.</summary>
    public double P99SchedulingLatencyUs { get; init; }

    /// <summary>Медиана scheduling-задержки, мкс.</summary>
    public double MedianSchedulingLatencyUs { get; init; }

    /// <summary>Джиттер таймера на целевом разрешении, мкс.</summary>
    public double TimerJitterUs { get; init; }

    /// <summary>Минимальное стабильное разрешение таймера, которое удалось получить, мс.</summary>
    public double StableTimerResolutionMs { get; init; }

    /// <summary>Рекомендуемый тип профиля по итогам бенчмарка.</summary>
    public ProfileKind RecommendedProfile { get; init; }

    /// <summary>Человекочитаемое заключение на русском языке с пояснениями.</summary>
    public string Summary { get; init; } = string.Empty;
}
