namespace AntiLagNext.Core.Models;

/// <summary>
/// Границы разрешения системного таймера (в единицах по 100 нс, как в NT API).
/// 1 мс = 10 000 единиц; 0.5 мс = 5 000 единиц; 15.625 мс = 156 250 единиц.
/// </summary>
public sealed class TimerCaps
{
    /// <summary>Максимально доступное разрешение (минимальный период в 100-нс единицах).</summary>
    public uint MinimumPeriod { get; init; }

    /// <summary>Минимально доступное разрешение (максимальный период в 100-нс единицах).</summary>
    public uint MaximumPeriod { get; init; }

    /// <summary>Минимальное разрешение в миллисекундах.</summary>
    public double MinimumMs => MinimumPeriod / 10_000.0;

    /// <summary>Максимальное разрешение в миллисекундах.</summary>
    public double MaximumMs => MaximumPeriod / 10_000.0;

    public override string ToString() => $"{MinimumMs:F3}–{MaximumMs:F3} мс";
}

/// <summary>
/// Текущее состояние управления таймером.
/// </summary>
public sealed class TimerState
{
    /// <summary>Доступные границы.</summary>
    public TimerCaps Caps { get; init; } = new();

    /// <summary>Запрошенное разрешение в 100-нс единицах (что пытались выставить).</summary>
    public uint DesiredPeriod100Ns { get; init; }

    /// <summary>Фактически установленное разрешение в 100-нс единицах (по NtSetTimerResolution).</summary>
    public uint ActualPeriod100Ns { get; init; }

    /// <summary>Измеренный максимальный джиттер за цикл проверки стабильности, в микросекундах.</summary>
    public double MeasuredJitterUs { get; init; }

    /// <summary>true, если таймер удерживается приложением (фоновый «демон» активен).</summary>
    public bool IsActive { get; init; }

    /// <summary>Запрошенное разрешение в миллисекундах.</summary>
    public double DesiredMs => DesiredPeriod100Ns / 10_000.0;

    /// <summary>Фактическое разрешение в миллисекундах.</summary>
    public double ActualMs => ActualPeriod100Ns / 10_000.0;

    public override string ToString()
        => IsActive ? $"Таймер активен: {ActualMs:F3} мс (джиттер {MeasuredJitterUs:F1} мкс)" : "Таймер не активен";
}
