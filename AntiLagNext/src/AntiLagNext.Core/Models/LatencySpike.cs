namespace AntiLagNext.Core.Models;

/// <summary>Зона качества latency (как у DPC Latency Checker).</summary>
public enum LatencyZone
{
    Green = 0,
    Yellow = 1,
    Red = 2
}

/// <summary>
/// Зафиксированный пик / событие высокого latency (user-mode proxy, не kernel DPC).
/// </summary>
public sealed class LatencySpike
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Latency в микросекундах.</summary>
    public double LatencyUs { get; init; }

    /// <summary>То же в миллисекундах (удобно читать «высокий мс»).</summary>
    public double LatencyMs => LatencyUs / 1000.0;

    public LatencyZone Zone { get; init; }

    /// <summary>CPU системы на момент пика, %.</summary>
    public float CpuPercent { get; init; }

    /// <summary>Самый «тяжёлый» процесс по WorkingSet (эвристика, не DPC-виновник).</summary>
    public string? TopProcessHint { get; init; }

    /// <summary>Сколько подряд red-сэмплов накопилось к моменту события.</summary>
    public int SustainedRedCount { get; init; }

    public string ZoneLabel => Zone switch
    {
        LatencyZone.Green => "GREEN",
        LatencyZone.Yellow => "YELLOW",
        LatencyZone.Red => "RED",
        _ => "?"
    };

    public string DisplayLine =>
        $"{Timestamp:HH:mm:ss.fff}  {LatencyUs,7:F0} µs ({LatencyMs:F3} мс)  [{ZoneLabel}]" +
        (string.IsNullOrEmpty(TopProcessHint) ? "" : $"  · {TopProcessHint}");
}

/// <summary>Итоговая статистика сессии мониторинга.</summary>
public sealed class LatencySessionStats
{
    public int SampleCount { get; init; }
    public double MaxUs { get; init; }
    public double AvgUs { get; init; }
    public double P99Us { get; init; }
    public double MedianUs { get; init; }
    public int YellowCount { get; init; }
    public int RedCount { get; init; }
    public int SpikeEventCount { get; init; }
    public double PercentInRed { get; init; }
    public double PercentInYellow { get; init; }
    public TimeSpan Duration { get; init; }

    public string SummaryRu =>
        SampleCount == 0
            ? "Нет данных. Нажмите «Старт»."
            : $"Сэмплов: {SampleCount} · max {MaxUs:F0} µs · p99 {P99Us:F0} µs · med {MedianUs:F0} µs · " +
              $"red {PercentInRed:F1}% · yellow {PercentInYellow:F1}% · событий: {SpikeEventCount} · {Duration:mm\\:ss}";
}
