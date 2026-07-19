using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AntiLagNext.Ui.Services;

/// <summary>
/// Ensures only one Photino UI process runs. A second launch signals the first
/// instance to restore/focus the main window (including when minimized to tray).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>Named kernel objects — Local\ so session isolation; unique per product.</summary>
    public const string MutexName = @"Local\AntiLagNext.Ui.SingleInstance.v1";
    public const string ShowEventName = @"Local\AntiLagNext.Ui.ShowExisting.v1";
    public const string WindowTitle = "AntiLag Next";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _listener;
    private readonly Action _onShowRequested;
    private bool _owned;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent, Action onShowRequested)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _onShowRequested = onShowRequested;
        _owned = true;

        _listener = new Thread(ListenForShowRequests)
        {
            IsBackground = true,
            Name = "AntiLag-SingleInstance"
        };
        _listener.Start();
    }

    /// <summary>
    /// Try to become the sole UI instance.
    /// Returns null if another instance already owns the mutex (caller should exit after ActivateExisting).
    /// Handles abandoned mutex after a crash so the app can start again.
    /// </summary>
    public static SingleInstance? TryEnter(Action onShowRequested)
    {
        Mutex? mutex = null;
        try
        {
            // initiallyOwned: false — acquire explicitly so AbandonedMutexException is catchable
            mutex = new Mutex(initiallyOwned: false, MutexName, out _);
            bool owns;
            try
            {
                owns = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // Previous process died without release — we now own the mutex
                owns = true;
                Trace.TraceWarning("SingleInstance: recovered abandoned mutex (previous process crashed).");
            }

            if (!owns)
            {
                try { mutex.Dispose(); } catch { /* ignore */ }
                return null;
            }

            EventWaitHandle showEvent;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch
            {
                try { mutex.ReleaseMutex(); } catch { /* ignore */ }
                try { mutex.Dispose(); } catch { /* ignore */ }
                throw;
            }

            return new SingleInstance(mutex, showEvent, onShowRequested);
        }
        catch
        {
            if (mutex != null)
            {
                try { mutex.Dispose(); } catch { /* ignore */ }
            }
            throw;
        }
    }

    /// <summary>Second instance: wake primary + restore window by title if possible.</summary>
    public static void ActivateExisting()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowEventName);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Primary may still be starting — fall through to FindWindow retries
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("SingleInstance.ActivateExisting event: {0}", ex.Message);
        }

        // Retry: primary may still be creating the Photino window
        for (int i = 0; i < 15; i++)
        {
            if (TryFocusWindowByTitle())
                return;
            Thread.Sleep(100);
        }
    }

    /// <returns>true if a matching top-level window was found and restored.</returns>
    public static bool TryFocusWindowByTitle()
    {
        try
        {
            IntPtr hwnd = FindWindowW(null, WindowTitle);
            if (hwnd == IntPtr.Zero) return false;
            TrayService.ShowWindowRestore(hwnd);
            SetForegroundWindow(hwnd);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("SingleInstance.TryFocusWindowByTitle: {0}", ex.Message);
            return false;
        }
    }

    private void ListenForShowRequests()
    {
        var handles = new WaitHandle[] { _showEvent, _cts.Token.WaitHandle };
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                int which = WaitHandle.WaitAny(handles, Timeout.Infinite);
                if (which != 0) break; // cancelled
                try { _onShowRequested(); }
                catch (Exception ex)
                {
                    Trace.TraceWarning("SingleInstance show callback: {0}", ex.Message);
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("SingleInstance listener: {0}", ex.Message);
            }
        }
    }

    public void Dispose()
    {
        if (!_owned) return;
        _owned = false;
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _showEvent.Set(); } catch { /* ignore */ } // unblock WaitAny
        try
        {
            if (_listener.IsAlive)
                _listener.Join(500);
        }
        catch { /* ignore */ }
        try { _showEvent.Dispose(); } catch { /* ignore */ }
        try
        {
            _mutex.ReleaseMutex();
        }
        catch { /* not owned / abandoned */ }
        try { _mutex.Dispose(); } catch { /* ignore */ }
        try { _cts.Dispose(); } catch { /* ignore */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
