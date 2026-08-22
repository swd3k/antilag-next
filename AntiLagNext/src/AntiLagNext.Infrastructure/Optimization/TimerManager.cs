using Microsoft.Win32;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// System timer resolution: timeBeginPeriod + NtSetTimerResolution hold.
/// Picks the finest stable candidate near the profile target (not a blind 0.5 ms).
///
/// Windows 11 22H2+: resolution is per-process. Games inherit it only after
/// GlobalTimerResolutionRequests=1 (catalog tweak, reboot). This manager reports
/// that scope honestly; it does not dual-write the kernel key (TweakCatalog owns it).
/// </summary>
public sealed class TimerManager : ITimerManager
{
    private readonly object _lock = new();
    private TimerState _state;
    private readonly int _osBuild;
    private readonly bool _globalRequestsAtStart;
    private uint _mmPeriod;

    private const double MaxAcceptableJitterUs = 250.0;
    private const int StabilityIterations = 80;
    private const double ProbeWaitMs = 1.0;

    public TimerManager()
    {
        _osBuild = NtDll.QueryBuildNumber();
        _globalRequestsAtStart = ReadGlobalTimerRequests() == 1;
        var caps = GetCaps();
        _state = new TimerState
        {
            Caps = caps,
            IsActive = false,
            OsBuild = _osBuild,
            Scope = "released"
        };
    }

    public TimerState CurrentState
    {
        get { lock (_lock) return _state; }
    }

    public event EventHandler<TimerState>? StateChanged;

    public TimerCaps GetCaps()
    {
        uint status = NtDll.NtQueryTimerResolution(out uint a, out uint b, out _);
        if (status != 0)
            return new TimerCaps { MinimumPeriod = 5000, MaximumPeriod = 156250 };

        uint fine = Math.Min(a, b);
        uint coarse = Math.Max(a, b);
        if (fine == 0) fine = 5000;
        if (coarse == 0) coarse = 156250;
        return new TimerCaps { MinimumPeriod = fine, MaximumPeriod = coarse };
    }

    public async Task<OperationResult<TimerState>> TuneAsync(double targetMs, CancellationToken cancellationToken = default)
    {
        try
        {
            var caps = GetCaps();
            var candidates = BuildCandidateList(targetMs, caps);

            BeginMultimediaPeriod(targetMs);

            uint bestPeriod = 0;
            uint bestActual = 0;
            double bestJitter = double.MaxValue;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint status = NtDll.NtSetTimerResolution(candidate, true, out uint actual);
                if (status != 0) continue;

                double jitter = await MeasureJitterAsync(cancellationToken).ConfigureAwait(false);
                if (jitter < bestJitter)
                {
                    bestJitter = jitter;
                    bestPeriod = candidate;
                    bestActual = actual;
                }

                if (jitter <= MaxAcceptableJitterUs) break;
            }

            if (bestPeriod == 0)
            {
                EndMultimediaPeriod();
                return OperationResult<TimerState>.Fail(
                    "Could not find a stable timer resolution.",
                    detail: "All candidates failed with NTSTATUS errors.");
            }

            string scope;
            lock (_lock)
            {
                scope = GlobalTimerPolicy.ScopeKey(_osBuild, _globalRequestsAtStart, timerHeld: true);
                _state = new TimerState
                {
                    Caps = caps,
                    DesiredPeriod100Ns = bestPeriod,
                    ActualPeriod100Ns = bestActual,
                    MeasuredJitterUs = bestJitter,
                    IsActive = true,
                    Scope = scope,
                    OsBuild = _osBuild
                };
            }
            StateChanged?.Invoke(this, CurrentState);

            string extra = scope switch
            {
                "pendingReboot" => " Games inherit after reboot (GlobalTimerResolutionRequests).",
                "local" => " This process only (Win11 22H2+).",
                _ => " Global (games inherit)."
            };

            return OperationResult<TimerState>.Ok(
                CurrentState,
                $"Timer: {CurrentState.ActualMs:F3} ms (jitter {bestJitter:F1} µs).{extra}");
        }
        catch (OperationCanceledException)
        {
            EndMultimediaPeriod();
            return OperationResult<TimerState>.Fail("Timer calibration cancelled.");
        }
        catch (Exception ex)
        {
            EndMultimediaPeriod();
            return OperationResult<TimerState>.Fail("Timer resolution calibration failed.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult Release()
    {
        try
        {
            EndMultimediaPeriod();
            NtDll.NtSetTimerResolution(0, false, out _);
            lock (_lock)
            {
                _state = new TimerState
                {
                    Caps = _state.Caps,
                    IsActive = false,
                    Scope = "released",
                    OsBuild = _osBuild
                };
            }
            StateChanged?.Invoke(this, CurrentState);
            return OperationResult.Ok("Timer released; system will return to default resolution.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Could not release timer.", detail: ex.Message, ex: ex);
        }
    }

    private void BeginMultimediaPeriod(double targetMs)
    {
        EndMultimediaPeriod();
        uint ms = (uint)Math.Clamp((int)Math.Round(Math.Max(targetMs, 1.0)), 1, 16);
        try
        {
            if (WinMM.timeBeginPeriod(ms) == 0)
                _mmPeriod = ms;
        }
        catch
        {
            _mmPeriod = 0;
        }
    }

    private void EndMultimediaPeriod()
    {
        if (_mmPeriod == 0) return;
        try { WinMM.timeEndPeriod(_mmPeriod); } catch { /* ignore */ }
        _mmPeriod = 0;
    }

    private static int? ReadGlobalTimerRequests()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", writable: false);
            if (key?.GetValue("GlobalTimerResolutionRequests") is int i)
                return i;
            if (key?.GetValue("GlobalTimerResolutionRequests") is uint u)
                return unchecked((int)u);
        }
        catch
        {
            /* not elevated / missing */
        }
        return null;
    }

    private static List<uint> BuildCandidateList(double targetMs, TimerCaps caps)
    {
        var set = new SortedSet<uint>();
        for (double ms = Math.Max(targetMs, caps.MinimumMs); ms <= Math.Min(1.0, caps.MaximumMs); ms += 0.1)
            set.Add((uint)(ms * 10_000));
        set.Add(caps.MinimumPeriod);
        set.Add(10_000);
        return set.ToList();
    }

    /// <summary>
    /// Scheduling jitter via waitable-timer 1 ms waits (same probe family as MonitoringService),
    /// not SpinWait — spin variance is not scheduling latency.
    /// </summary>
    private static async Task<double> MeasureJitterAsync(CancellationToken ct)
    {
        if (!Kernel32.QueryPerformanceFrequency(out long freq) || freq <= 0)
            return double.MaxValue;

        return await Task.Run(() =>
        {
            var intervals = new double[StabilityIterations];
            IntPtr hTimer = Kernel32.CreateWaitableTimer(IntPtr.Zero, true, null);
            long due = unchecked((long)(-ProbeWaitMs * 10_000)); // relative 100-ns

            try
            {
                for (int i = 0; i < StabilityIterations; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    Kernel32.QueryPerformanceCounter(out long prev);
                    if (hTimer != IntPtr.Zero
                        && Kernel32.SetWaitableTimer(hTimer, ref due, 0, null, IntPtr.Zero, false))
                    {
                        Kernel32.WaitForSingleObject(hTimer, 50);
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                    Kernel32.QueryPerformanceCounter(out long now);
                    intervals[i] = (now - prev) * 1_000_000.0 / freq;
                }
            }
            finally
            {
                if (hTimer != IntPtr.Zero)
                    Kernel32.CloseHandle(hTimer);
            }

            Array.Sort(intervals);
            double median = intervals[intervals.Length / 2];
            double maxDeviation = 0;
            foreach (double x in intervals)
            {
                double d = Math.Abs(x - median);
                if (d > maxDeviation) maxDeviation = d;
            }
            return maxDeviation;
        }, ct).ConfigureAwait(false);
    }
}
