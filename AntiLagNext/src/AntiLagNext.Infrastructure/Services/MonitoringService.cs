using System.Diagnostics;
using System.Runtime.InteropServices;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Мониторинг latency. Замер выполняется на выделенном High-потоке, без блокировки UI.
/// Пачка sub-samples → median (график) + max (нагрузка). Idle-низкий / interactive-высокий — норма.
/// </summary>
public sealed class MonitoringService : IMonitoringService, IDisposable
{
    private readonly ITimerManager _timerManager;
    private readonly IPowerManager _power;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _running;

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasCpuBaseline;

    /// <summary>Сколько 1ms-проб в одном тике (нечётное → удобная медиана).</summary>
    private const int ProbesPerTick = 7;

    /// <summary>Если max/median > этого — считаем «под нагрузкой».</summary>
    private const double LoadSpikeRatio = 2.5;

    public event EventHandler<MonitoringSample>? SampleArrived;

    public PowerSource CurrentPowerSource => _power.GetCurrentPowerSource();

    public MonitoringService(ITimerManager timer, IPowerManager power)
    {
        _timerManager = timer;
        _power = power;
    }

    public void Start(TimeSpan interval)
    {
        Stop();
        lock (_lock)
        {
            _hasCpuBaseline = false;
            _running = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var period = interval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(150) : interval;

            _loopTask = Task.Factory.StartNew(
                () => RunLoop(period, token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
        }

        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _loopTask = null;
    }

    private void RunLoop(TimeSpan period, CancellationToken token)
    {
        try
        {
            Thread.CurrentThread.Name = "AntiLag-LatencyProbe";
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            // Не UI, не ThreadPool — меньше джиттера от других callback'ов
        }
        catch { /* priority may fail */ }

        var sw = Stopwatch.StartNew();
        while (!token.IsCancellationRequested)
        {
            var t0 = sw.Elapsed;
            try
            {
                if (!_running) break;
                var sample = CaptureSample();
                SampleArrived?.Invoke(this, sample);
            }
            catch
            {
                // never kill probe loop
            }

            var elapsed = sw.Elapsed - t0;
            var sleep = period - elapsed;
            if (sleep > TimeSpan.Zero)
            {
                try { Task.Delay(sleep, token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private MonitoringSample CaptureSample()
    {
        var probes = MeasureProbeBatch(ProbesPerTick);
        Array.Sort(probes);
        double median = probes[probes.Length / 2];
        double min = probes[0];
        double max = probes[^1];
        float cpu = MeasureSystemCpuPercent();
        bool underLoad = cpu >= 25f
                         || (median > 1 && max >= median * LoadSpikeRatio)
                         || max >= 150;

        return new MonitoringSample
        {
            Timestamp = DateTime.UtcNow,
            SchedulingLatencyUs = median,
            SchedulingLatencyMaxUs = max,
            SchedulingLatencyMinUs = min,
            ProbeCount = probes.Length,
            SystemUnderLoad = underLoad,
            TimerResolutionMs = QueryTimerMs(),
            FrameTimeMs = null,
            CpuUsagePercent = cpu,
            UsedMemoryMb = GetUsedMemoryMb(),
            PowerSource = _power.GetCurrentPowerSource()
        };
    }

    /// <summary>
    /// Несколько коротких waitable-timer проб. Одна проба в idle часто ≈0 и «не скачет»;
    /// max пачки отражает интерактивную нагрузку.
    /// </summary>
    private static double[] MeasureProbeBatch(int count)
    {
        var results = new double[count];
        if (!Kernel32.QueryPerformanceFrequency(out long freq) || freq <= 0)
            return results;

        // Один handle на пачку — меньше syscalls
        IntPtr hTimer = Kernel32.CreateWaitableTimer(IntPtr.Zero, true, null);
        if (hTimer == IntPtr.Zero) return results;

        try
        {
            for (int i = 0; i < count; i++)
            {
                long due = -10_000; // 1 ms relative
                if (!Kernel32.SetWaitableTimer(hTimer, ref due, 0, null, IntPtr.Zero, false))
                {
                    results[i] = 0;
                    continue;
                }

                Kernel32.QueryPerformanceCounter(out long t0);
                uint wait = Kernel32.WaitForSingleObject(hTimer, 50);
                Kernel32.QueryPerformanceCounter(out long t1);

                if (wait != Kernel32.WaitObject0)
                {
                    results[i] = 50_000; // timeout → bad
                    continue;
                }

                double elapsedUs = (t1 - t0) * 1_000_000.0 / freq;
                results[i] = Math.Max(0, elapsedUs - 1000.0);
            }
        }
        finally
        {
            Kernel32.CloseHandle(hTimer);
        }

        return results;
    }

    private double QueryTimerMs()
    {
        try
        {
            var state = _timerManager.CurrentState;
            if (state.IsActive && state.ActualPeriod100Ns > 0)
                return state.ActualMs;

            NtDll.NtQueryTimerResolution(out _, out _, out uint current);
            return current / 10_000.0;
        }
        catch
        {
            return 15.625;
        }
    }

    private float MeasureSystemCpuPercent()
    {
        try
        {
            if (!Kernel32.GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return 0;

            ulong idle = idleFt.ToUInt64();
            ulong kernel = kernelFt.ToUInt64();
            ulong user = userFt.ToUInt64();

            if (!_hasCpuBaseline)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _hasCpuBaseline = true;
                return 0;
            }

            ulong idleDelta = idle - _prevIdle;
            ulong kernelDelta = kernel - _prevKernel;
            ulong userDelta = user - _prevUser;
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            ulong total = kernelDelta + userDelta;
            if (total == 0) return 0;
            ulong busy = total > idleDelta ? total - idleDelta : 0;
            double pct = 100.0 * busy / total;
            return (float)Math.Clamp(pct, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static float GetUsedMemoryMb()
    {
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                ulong used = status.ullTotalPhys - status.ullAvailPhys;
                return used / (1024f * 1024f);
            }
        }
        catch { /* ignore */ }

        return Process.GetCurrentProcess().WorkingSet64 / (1024f * 1024f);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public void Dispose() => Stop();
}
