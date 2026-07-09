namespace AntiLagNext.Core.Models;

/// <summary>
/// Топология процессора с гетерогенной архитектурой (Intel big.LITTLE / P-cores + E-cores).
/// Определяется через GetLogicalProcessorInformationEx по флагу EfficiencyClass.
/// </summary>
public sealed class HybridCpuTopology
{
    /// <summary>Всего логических процессоров в системе.</summary>
    public int LogicalProcessorCount { get; init; }

    /// <summary>Количество производительных ядер (P-cores, efficiency class = 0).</summary>
    public int PerformanceCoreCount { get; init; }

    /// <summary>Количество энергоэффективных ядер (E-cores, efficiency class = 1).</summary>
    public int EfficientCoreCount { get; init; }

    /// <summary>Признак гетерогенности: true, если есть хотя бы одно E-core.</summary>
    public bool IsHybrid => EfficientCoreCount > 0;

    public override string ToString()
        => IsHybrid
            ? $"{LogicalProcessorCount} потоков: {PerformanceCoreCount} P-core + {EfficientCoreCount} E-core"
            : $"{LogicalProcessorCount} потоков (однородный CPU)";
}
