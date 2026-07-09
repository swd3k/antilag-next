namespace AntiLagNext.Core.Models;

/// <summary>
/// Well-known GUID'ы схем электропитания и их настроек (PowerCfg).
/// Источник: Microsoft PowerCfg documentation. Эти же значения использует оригинальный AntiLag.
/// В AntiLag Next применяются через PowerWriteAC/DCValueIndex вместо шелла.
/// </summary>
public static class PowerGuids
{
    // --- Схемы электропитания ---

    /// <summary>Сбалансированная схема (Balanced).</summary>
    public static readonly Guid SchemeBalanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    /// <summary>Высокая производительность (High Performance).</summary>
    public static readonly Guid SchemeHighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    /// <summary>Максимальная производительность (Ultimate Performance) — скрытая схема.</summary>
    public static readonly Guid SchemeUltimatePerformance = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>Энергосберегающая схема (Power Saver).</summary>
    public static readonly Guid SchemePowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

    // --- Подгруппы настроек ---

    /// <summary>Подгруппа: настройки процессора.</summary>
    public static readonly Guid SubProcessor = Guid.Parse("54533251-82be-4824-96c1-47b60b740d00");

    /// <summary>Подгруппа: настройки жёсткого диска.</summary>
    public static readonly Guid SubDisk = Guid.Parse("0012ee47-9041-4b5d-9b77-535fba8b1442");

    /// <summary>Подгруппа: USB.</summary>
    public static readonly Guid SubUsb = Guid.Parse("2a737441-1930-4402-8d77-b2bebba308a3");

    /// <summary>Подгруппа: спящий режим.</summary>
    public static readonly Guid SubSleep = Guid.Parse("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");

    /// <summary>Подгруппа: PCI Express (ASPM).</summary>
    public static readonly Guid SubPciExpress = Guid.Parse("501a4d13-42af-4429-9fd1-a821f2c0e6dd");

    /// <summary>Link State Power Management (ASPM) — 0 = Off.</summary>
    public static readonly Guid PciExpressAspm = Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5");

    // --- Настройки процессора (внутри SUB_PROCESSOR) ---

    /// <summary>Минимальный процент активных ядер (CPMINCORES) — 100% = парковка отключена.</summary>
    public static readonly Guid ProcessorCoreParkingMinCores = Guid.Parse("0cc5b647-c1df-4637-891a-dec35c318583");

    /// <summary>Максимальный процент активных ядер (CPMAXCORES).</summary>
    public static readonly Guid ProcessorCoreParkingMaxCores = Guid.Parse("ea062031-c029-4111-9366-13a690454524");

    /// <summary>Режим повышения производительности (Performance Boost Mode).</summary>
    public static readonly Guid ProcessorPerformanceBoostMode = Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7");

    /// <summary>Минимальное состояние процессора (%) — PROCTHROTTLEMIN.</summary>
    public static readonly Guid ProcessorMinimumState = Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964c");

    /// <summary>Максимальное состояние процессора (%) — PROCTHROTTLEMAX.</summary>
    public static readonly Guid ProcessorMaximumState = Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ec");

    // --- Реестр для Game Mode / Game Bar / HAGS (твики) ---

    public const string GameBarKey = @"SOFTWARE\Microsoft\GameBar";
    public const string GameConfigStoreKey = @"System\GameConfigStore";
    public const string GameDvrKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
    public const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string PriorityControlKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
}

/// <summary>
/// Описание одной настройки power-plan для UI/документации.
/// </summary>
public sealed record PowerSettingDescriptor(string DisplayName, Guid SubGroup, Guid Setting, uint RecommendedAc, uint RecommendedDc, string Explanation);
