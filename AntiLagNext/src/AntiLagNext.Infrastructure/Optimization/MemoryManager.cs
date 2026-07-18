using System.Diagnostics;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// Очистка рабочего набора (Empty Working Set) фоновых процессов.
/// Не трогает критические системные процессы и список исключений из UI.
/// </summary>
public sealed class MemoryManager : IMemoryManager
{
    /// <summary>Всегда исключаемые системные процессы (защита от нестабильности).</summary>
    private static readonly HashSet<string> HardExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "winlogon", "Memory Compression",
        "AntiLagNext", "MsMpEng", "SecurityHealthService", "SearchIndexer"
    };

    public OperationResult<MemoryCleanupStats> EmptyWorkingSets(IReadOnlyCollection<string> exclusions)
    {
        try
        {
            var exclude = new HashSet<string>(HardExclusions, StringComparer.OrdinalIgnoreCase);
            foreach (var e in exclusions)
            {
                if (string.IsNullOrWhiteSpace(e)) continue;
                exclude.Add(Path.GetFileNameWithoutExtension(e.Trim()));
            }

            int trimmed = 0, skipped = 0;
            long bytesFreed = 0;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName;
                    if (exclude.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    // Не трогаем текущий процесс
                    if (proc.Id == Environment.ProcessId)
                    {
                        skipped++;
                        continue;
                    }

                    long before = 0;
                    try { before = proc.WorkingSet64; } catch { /* access denied */ }

                    IntPtr handle = ProcessNative.OpenProcess(
                        ProcessNative.PROCESS_SET_QUOTA | ProcessNative.PROCESS_QUERY_INFORMATION | ProcessNative.PROCESS_VM_READ,
                        false,
                        (uint)proc.Id);

                    if (handle == IntPtr.Zero)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        if (Psapi.EmptyWorkingSet(handle))
                        {
                            trimmed++;
                            try
                            {
                                proc.Refresh();
                                long after = proc.WorkingSet64;
                                if (before > after) bytesFreed += before - after;
                            }
                            catch { /* ignore */ }
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    finally
                    {
                        Kernel32.CloseHandle(handle);
                    }
                }
                catch
                {
                    skipped++;
                }
                finally
                {
                    try { proc.Dispose(); } catch { /* ignore */ }
                }
            }

            var stats = new MemoryCleanupStats
            {
                ProcessesTrimmed = trimmed,
                BytesFreed = bytesFreed,
                ProcessesSkipped = skipped
            };

            return OperationResult<MemoryCleanupStats>.Ok(
                stats,
                $"Memory: trimmed {trimmed} processes, ≈ {bytesFreed / (1024.0 * 1024.0):F1} MB freed, skipped {skipped}.");
        }
        catch (Exception ex)
        {
            return OperationResult<MemoryCleanupStats>.Fail("Memory cleanup failed.", detail: ex.Message, ex: ex);
        }
    }
}
