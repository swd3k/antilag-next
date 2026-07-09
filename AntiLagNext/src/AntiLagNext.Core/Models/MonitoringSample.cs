using AntiLagNext.Core.Enums;

namespace AntiLagNext.Core.Models;

/// <summary>
/// Один отсчёт мониторинга. Scheduling latency — proxy (waitable timer + QPC), не kernel DPC.
/// </summary>
public sealed class MonitoringSample
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Основное значение для графика: медиана пачки коротких замеров (µs).
    /// Стабильнее одиночного wait, меньше «ложных нулей» в idle.
    /// </summary>
    public double SchedulingLatencyUs { get; init; }

    /// <summary>Худший замер в пачке (µs) — ближе к тому, что чувствует интерактив.</summary>
    public double SchedulingLatencyMaxUs { get; init; }

    /// <summary>Минимальный замер в пачке (µs).</summary>
    public double SchedulingLatencyMinUs { get; init; }

    /// <summary>Сколько sub-samples в пачке.</summary>
    public int ProbeCount { get; init; }

    /// <summary>
    /// true, если система похоже под нагрузкой (высокий CPU или max >> median).
    /// Idle-flat + interactive spikes — норма, не баг AntiLag.
    /// </summary>
    public bool SystemUnderLoad { get; init; }

    public double TimerResolutionMs { get; init; }
    public double? FrameTimeMs { get; init; }
    public float CpuUsagePercent { get; init; }
    public float UsedMemoryMb { get; init; }
    public PowerSource PowerSource { get; init; }
}

public sealed class BenchmarkResult
{
    public double MaxSchedulingLatencyUs { get; init; }
    public double P99SchedulingLatencyUs { get; init; }
    public double MedianSchedulingLatencyUs { get; init; }
    public double TimerJitterUs { get; init; }
    public double StableTimerResolutionMs { get; init; }
    public ProfileKind RecommendedProfile { get; init; }
    public string Summary { get; init; } = string.Empty;
}
