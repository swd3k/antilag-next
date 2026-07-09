using System.Diagnostics;
using System.Runtime.InteropServices;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Мониторинг задержек в реальном времени.
/// Scheduling latency: waitable timer + QPC (прокси DPC/ISR latency).
/// Timer resolution: NtQueryTimerResolution.
/// CPU: system-wide через GetSystemTimes (не process CPU).
/// RAM: GlobalMemoryStatusEx.
/// </summary>
public sealed class MonitoringService : IMonitoringService, IDisposable
{
    private readonly ITimerManager _timerManager;
    private readonly IPowerManager _power;
    private System.Threading.Timer? _pollTimer;
    private readonly object _lock = new();
    private bool _running;

    // GetSystemTimes deltas
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasCpuBaseline;

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
            _pollTimer = new System.Threading.Timer(OnTick, null, TimeSpan.Zero, interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }

    private void OnTick(object? state)
    {
        if (!_running) return;
        try
        {
            var sample = new MonitoringSample
            {
                Timestamp = DateTime.UtcNow,
                SchedulingLatencyUs = MeasureSchedulingLatencyUs(),
                TimerResolutionMs = QueryTimerMs(),
                FrameTimeMs = null,
                CpuUsagePercent = MeasureSystemCpuPercent(),
                UsedMemoryMb = GetUsedMemoryMb(),
                PowerSource = _power.GetCurrentPowerSource()
            };

            SampleArrived?.Invoke(this, sample);
        }
        catch
        {
            // monitoring must never crash the app
        }
    }

    private static double MeasureSchedulingLatencyUs()
    {
        if (!Kernel32.QueryPerformanceFrequency(out long freq) || freq <= 0)
            return 0;

        IntPtr hTimer = Kernel32.CreateWaitableTimer(IntPtr.Zero, true, null);
        if (hTimer == IntPtr.Zero) return 0;

        try
        {
            long due = -10_000; // 1 ms relative
            if (!Kernel32.SetWaitableTimer(hTimer, ref due, 0, null, IntPtr.Zero, false))
                return 0;

            Kernel32.QueryPerformanceCounter(out long t0);
            Kernel32.WaitForSingleObject(hTimer, 50);
            Kernel32.QueryPerformanceCounter(out long t1);

            double elapsedUs = (t1 - t0) * 1_000_000.0 / freq;
            return Math.Max(0, elapsedUs - 1000.0);
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

    /// <summary>
    /// System-wide CPU % через GetSystemTimes.
    /// busy = (kernel - idle) + user; total = kernel + user (kernel already includes idle).
    /// </summary>
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

            // kernel includes idle; total work window = kernel + user
            ulong total = kernelDelta + userDelta;
            if (total == 0) return 0;

            ulong busy = total > idleDelta ? total - idleDelta : 0;
            double pct = 100.0 * busy / total;
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            return (float)pct;
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
