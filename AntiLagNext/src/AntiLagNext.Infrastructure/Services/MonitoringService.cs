using System.Diagnostics;
using System.Runtime.InteropServices;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Scheduling-latency proxy (waitable timer + QPC), не network ping / input lag.
/// HiPri dedicated thread; sleep через waitable timer (не Task.Delay).
/// Idle-низкий / interactive-высокий — ожидаемо; max важнее median под вводом.
/// </summary>
public sealed class MonitoringService : IMonitoringService, IDisposable
{
    private readonly ITimerManager _timerManager;
    private readonly IPowerManager _power;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _running;
    private ProcessPriorityClass? _savedProcessPriority;

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasCpuBaseline;

    /// <summary>Число 1ms-проб при обычном (медленном) мониторинге.</summary>
    private const int ProbesPerTickNormal = 5;

    /// <summary>При interval ≤ 10ms — одна короткая проба, чтобы уложиться в 5ms тик графика.</summary>
    private const int ProbesPerTickFast = 1;

    private const double LoadSpikeRatio = 2.5;

    /// <summary>Целевой период тика (для UI: 5ms).</summary>
    private TimeSpan _period = TimeSpan.FromMilliseconds(150);

    /// <summary>true = high-frequency chart mode (1 probe / short sleep).</summary>
    private bool _fastMode;

    private int _probesPerTick = ProbesPerTickNormal;

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

            // Chart mode (≤50ms): single short probe, no 150ms floor.
            // Normal mode keeps ≥150ms to avoid self-noise on slow monitoring.
            if (interval <= TimeSpan.FromMilliseconds(50))
            {
                _fastMode = true;
                _probesPerTick = ProbesPerTickFast;
                // Clamp chart cadence to 5–50 ms
                double ms = Math.Clamp(interval.TotalMilliseconds, 5, 50);
                _period = TimeSpan.FromMilliseconds(ms);
                // CRITICAL: do NOT raise whole-process PriorityClass in chart mode —
                // Photino/WebView2 UI shares the process and freezes under High priority.
            }
            else
            {
                _fastMode = false;
                _probesPerTick = ProbesPerTickNormal;
                _period = interval < TimeSpan.FromMilliseconds(150)
                    ? TimeSpan.FromMilliseconds(150)
                    : interval;
                // Background monitoring only: process bump is acceptable outside chart UI
                RaiseProcessPriority();
            }

            _loopTask = Task.Factory.StartNew(
                () => RunLoop(_period, token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public void Stop()
    {
        Task? toJoin;
        lock (_lock)
        {
            _running = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
            toJoin = _loopTask;
            _loopTask = null;
        }

        // Never block Photino/UI thread waiting for probe exit — join off-thread.
        if (toJoin != null)
        {
            _ = Task.Run(() =>
            {
                try { toJoin.Wait(TimeSpan.FromMilliseconds(500)); } catch { /* ignore */ }
                RestoreProcessPriority();
            });
        }
        else
        {
            RestoreProcessPriority();
        }
    }

    private void RaiseProcessPriority()
    {
        try
        {
            var p = Process.GetCurrentProcess();
            _savedProcessPriority = p.PriorityClass;
            if (p.PriorityClass < ProcessPriorityClass.High)
                p.PriorityClass = ProcessPriorityClass.High;
        }
        catch
        {
            _savedProcessPriority = null;
        }
    }

    private void RestoreProcessPriority()
    {
        if (_savedProcessPriority is not { } saved) return;
        try { Process.GetCurrentProcess().PriorityClass = saved; } catch { /* ignore */ }
        _savedProcessPriority = null;
    }

    private void RunLoop(TimeSpan period, CancellationToken token)
    {
        try
        {
            Thread.CurrentThread.Name = "AntiLag-LatencyProbe";
            // AboveNormal is enough; Highest starves WebView2/UI in the same process
            Thread.CurrentThread.Priority = _fastMode
                ? ThreadPriority.AboveNormal
                : ThreadPriority.Highest;
        }
        catch { /* may fail */ }

        IntPtr hSleep = Kernel32.CreateWaitableTimer(IntPtr.Zero, true, null);
        var sw = Stopwatch.StartNew();

        try
        {
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
                    PreciseSleep(hSleep, sleep, token);
            }
        }
        finally
        {
            if (hSleep != IntPtr.Zero)
                Kernel32.CloseHandle(hSleep);
            try { Thread.CurrentThread.Priority = ThreadPriority.Normal; } catch { /* ignore */ }
        }
    }

    /// <summary>Sleep через waitable timer — меньше джиттера, чем Task.Delay/Thread.Sleep.</summary>
    private static void PreciseSleep(IntPtr hTimer, TimeSpan duration, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        double ms = duration.TotalMilliseconds;
        if (ms < 0.5) return;

        if (hTimer == IntPtr.Zero)
        {
            try { Thread.Sleep(duration); } catch { /* ignore */ }
            return;
        }

        // 100-ns units, negative = relative
        long due = -(long)(ms * 10_000.0);
        if (!Kernel32.SetWaitableTimer(hTimer, ref due, 0, null, IntPtr.Zero, false))
        {
            try { Thread.Sleep(duration); } catch { /* ignore */ }
            return;
        }

        uint waitMs = (uint)Math.Clamp(ms + 20, 1, 60_000);
        while (!token.IsCancellationRequested)
        {
            uint r = Kernel32.WaitForSingleObject(hTimer, Math.Min(waitMs, 50));
            if (r == Kernel32.WaitObject0) break;
            if (r == Kernel32.WaitTimeout && waitMs <= 50)
            {
                // slice wait to honour cancel
                waitMs = waitMs > 50 ? waitMs - 50 : 0;
                if (waitMs == 0) break;
            }
        }
    }

    // Fixed buffers — no per-tick heap alloc in probe path (max of normal batch size)
    private readonly double[] _probeBuffer = new double[ProbesPerTickNormal];

    private MonitoringSample CaptureSample()
    {
        int n = Math.Clamp(_probesPerTick, 1, _probeBuffer.Length);
        MeasureProbeBatchInto(_probeBuffer, n, _fastMode);
        // sort only used slice
        Array.Sort(_probeBuffer, 0, n);
        double median = _probeBuffer[n / 2];
        double min = _probeBuffer[0];
        double max = _probeBuffer[n - 1];

        // CPU / power are relatively expensive — sample every ~50ms in fast mode
        float cpu = 0;
        PowerSource power = PowerSource.Ac;
        double timerMs = 0;
        float memMb = 0;
        if (!_fastMode || (Environment.TickCount64 % 50) < 6)
        {
            cpu = MeasureSystemCpuPercent();
            power = _power.GetCurrentPowerSource();
            timerMs = QueryTimerMs();
            memMb = GetUsedMemoryMb();
        }
        else
        {
            cpu = _lastCpu;
            timerMs = _lastTimerMs;
            memMb = _lastMemMb;
            power = _lastPower;
        }

        _lastCpu = cpu;
        _lastTimerMs = timerMs;
        _lastMemMb = memMb;
        _lastPower = power;

        bool underLoad = cpu >= 20f
                         || (median > 1 && max >= median * LoadSpikeRatio)
                         || max >= 120;

        return new MonitoringSample
        {
            Timestamp = DateTime.UtcNow,
            SchedulingLatencyUs = median,
            SchedulingLatencyMaxUs = max,
            SchedulingLatencyMinUs = min,
            ProbeCount = n,
            SystemUnderLoad = underLoad,
            TimerResolutionMs = timerMs,
            FrameTimeMs = null,
            CpuUsagePercent = cpu,
            UsedMemoryMb = memMb,
            PowerSource = power
        };
    }

    private float _lastCpu;
    private double _lastTimerMs;
    private float _lastMemMb;
    private PowerSource _lastPower = PowerSource.Ac;

    /// <param name="count">Number of probes into results[0..count).</param>
    /// <param name="fast">
    /// Fast chart mode: target wait 0.5ms so one probe + overhead fits in a 5ms UI tick.
    /// Normal mode: 1ms wait (classic scheduling jitter proxy).
    /// </param>
    private void MeasureProbeBatchInto(double[] results, int count, bool fast)
    {
        Array.Clear(results);
        if (!Kernel32.QueryPerformanceFrequency(out long freq) || freq <= 0)
            return;

        IntPtr hTimer = Kernel32.CreateWaitableTimer(IntPtr.Zero, true, null);
        if (hTimer == IntPtr.Zero) return;

        // 100-ns units: 1ms = 10_000, 0.5ms = 5_000
        long dueUnits = fast ? -5_000L : -10_000L;
        double expectedUs = fast ? 500.0 : 1000.0;

        try
        {
            for (int i = 0; i < count; i++)
            {
                long due = dueUnits;
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
                    results[i] = 50_000;
                    continue;
                }

                double elapsedUs = (t1 - t0) * 1_000_000.0 / freq;
                results[i] = Math.Max(0, elapsedUs - expectedUs);
            }
        }
        finally
        {
            Kernel32.CloseHandle(hTimer);
        }
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
            return (float)Math.Clamp(100.0 * busy / total, 0, 100);
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
