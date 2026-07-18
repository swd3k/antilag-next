using System.Runtime.InteropServices;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// Управление парковкой ядер с учётом гетерогенной архитектуры (Intel P-cores / E-cores).
///
/// Топология: GetLogicalProcessorInformationEx(RelationProcessorCore).
/// EfficiencyClass: 0 = P-core (performance), 1 = E-core (efficiency).
///
/// Режимы:
/// - AllActive: CPMINCORES = 100% — парковка фактически отключена.
/// - KeepEfficientIdle: на гибриде CPMINCORES ниже 100%, чтобы E-cores могли парковаться.
/// - SystemDefault: без изменений.
/// </summary>
public sealed class CoreParkingManager : ICoreParkingManager
{
    private readonly IPowerManager _power;

    public CoreParkingManager(IPowerManager power) => _power = power;

    public OperationResult<HybridCpuTopology> DetectTopology()
    {
        try
        {
            uint len = 0;
            Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationProcessorCore, IntPtr.Zero, ref len);
            if (len == 0)
                return OperationResult<HybridCpuTopology>.Fail("GetLogicalProcessorInformationEx: размер буфера 0.");

            IntPtr buffer = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationProcessorCore, buffer, ref len))
                {
                    int err = Marshal.GetLastWin32Error();
                    return OperationResult<HybridCpuTopology>.Fail(
                        "GetLogicalProcessorInformationEx failed",
                        detail: Win32Result.FormatMessage(err));
                }

                int perfCores = 0, effCores = 0, logicalTotal = 0;
                int offset = 0;

                while (offset + 8 < len) // минимум Relationship + Size
                {
                    IntPtr current = IntPtr.Add(buffer, offset);
                    // Layout SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX:
                    // int Relationship (0), int Size, затем PROCESSOR_RELATIONSHIP
                    int relationship = Marshal.ReadInt32(current, 0);
                    int size = Marshal.ReadInt32(current, 4);
                    if (size <= 0) break;

                    // PROCESSOR_RELATIONSHIP: Flags(1) + EfficiencyClass(1) + Reserved(2) + GroupCount(2) + ...
                    // Offset внутри записи: 8 байт (Relationship+Size) + 0 Flags, +1 EfficiencyClass
                    byte efficiencyClass = Marshal.ReadByte(current, 8 + 1);
                    short groupCount = Marshal.ReadInt16(current, 8 + 4);

                    if (efficiencyClass == 1)
                        effCores++;
                    else
                        perfCores++;

                    logicalTotal += CountLogicalFromMask(current, groupCount);

                    offset += size;
                }

                var topology = new HybridCpuTopology
                {
                    LogicalProcessorCount = Math.Max(logicalTotal, Environment.ProcessorCount),
                    PerformanceCoreCount = Math.Max(perfCores, 1),
                    EfficientCoreCount = effCores
                };
                return OperationResult<HybridCpuTopology>.Ok(topology, topology.ToString());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            // Fallback: однородный CPU
            var fallback = new HybridCpuTopology
            {
                LogicalProcessorCount = Environment.ProcessorCount,
                PerformanceCoreCount = Environment.ProcessorCount,
                EfficientCoreCount = 0
            };
            return OperationResult<HybridCpuTopology>.Ok(fallback,
                $"Топология (fallback): {fallback}. Деталь: {ex.Message}");
        }
    }

    public OperationResult ApplyMode(Guid schemeGuid, CoreParkingMode mode)
    {
        try
        {
            if (mode == CoreParkingMode.SystemDefault)
                return OperationResult.Ok("Core parking: system default (unchanged).");

            // Снять скрытие CPMINCORES (best-effort)
            _power.UnhideSetting(PowerGuids.SubProcessor, PowerGuids.ProcessorCoreParkingMinCores);

            var topology = DetectTopology();
            bool isHybrid = topology.Success && topology.Value is { IsHybrid: true };

            // CPMINCORES: 100 = все активны. KeepEfficientIdle на гибриде ≈ 70% (P-cores + часть E).
            uint minCoresAc = mode == CoreParkingMode.AllActive || !isHybrid ? 100u : 70u;
            uint minCoresDc = mode == CoreParkingMode.AllActive ? 100u : 50u;

            var write = _power.WriteValue(
                schemeGuid,
                PowerGuids.SubProcessor,
                PowerGuids.ProcessorCoreParkingMinCores,
                acValue: minCoresAc,
                dcValue: minCoresDc);

            if (!write.Success) return write;

            // CPMAXCORES = 100%
            _power.WriteValue(
                schemeGuid,
                PowerGuids.SubProcessor,
                PowerGuids.ProcessorCoreParkingMaxCores,
                acValue: 100,
                dcValue: 100);

            // Aggressive boost только для AllActive
            if (mode == CoreParkingMode.AllActive)
            {
                _power.WriteValue(schemeGuid, PowerGuids.SubProcessor, PowerGuids.ProcessorPerformanceBoostMode,
                    acValue: 2, dcValue: 1);
            }

            string modeName = mode switch
            {
                CoreParkingMode.AllActive => "all cores active",
                CoreParkingMode.KeepEfficientIdle => isHybrid
                    ? "P-cores active, E-cores may park"
                    : "all cores active (homogeneous CPU)",
                _ => "unchanged"
            };
            return OperationResult.Ok($"Core parking: {modeName} (CPMINCORES AC={minCoresAc}%).");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Could not apply core activity mode.", detail: ex.Message, ex: ex);
        }
    }

    /// <summary>
    /// Подсчёт логических процессоров по GROUP_AFFINITY после заголовка записи.
    /// </summary>
    private static int CountLogicalFromMask(IntPtr entryPtr, short groupCount)
    {
        // После Relationship(4)+Size(4)+Flags(1)+Eff(1)+Res(2)+GroupCount(2) = 14, выравнивание до 16
        // Затем массивы GROUP_AFFINITY (Mask ulong + Group ushort + Reserved[3] ushort) ≈ 16 байт
        const int groupsOffset = 16;
        int groupSize = 16; // ULONG_PTR Mask + WORD Group + WORD Reserved[3] + pad
        int count = 0;
        for (int g = 0; g < Math.Max(groupCount, (short)1); g++)
        {
            try
            {
                IntPtr grpPtr = IntPtr.Add(entryPtr, groupsOffset + g * groupSize);
                ulong mask = (ulong)Marshal.ReadInt64(grpPtr);
                count += System.Numerics.BitOperations.PopCount(mask);
            }
            catch
            {
                count += 1;
            }
        }
        return count > 0 ? count : 1;
    }
}
