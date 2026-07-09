using System.Management;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Обнаружение запуска/закрытия игр по списку exe.
/// Использует WMI Win32_ProcessStartTrace / StopTrace (требует прав админа).
/// Fallback: polling Process.GetProcesses (если WMI недоступен).
/// </summary>
public sealed class GameDetectionService : IGameDetectionService, IDisposable
{
    private readonly object _lock = new();
    private HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private System.Threading.Timer? _pollTimer;
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? GameStarted;
    public event EventHandler<string>? GameStopped;

    public OperationResult Start(IReadOnlyCollection<string> executableNames)
    {
        Stop();
        lock (_lock)
        {
            _watched = new HashSet<string>(
                executableNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => Path.GetFileName(x.Trim())),
                StringComparer.OrdinalIgnoreCase);

            if (_watched.Count == 0)
                return OperationResult.Ok("Game Detection: список игр пуст — мониторинг не запущен.");
        }

        try
        {
            // WMI process start
            var startQuery = new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStartTrace");
            _startWatcher = new ManagementEventWatcher(startQuery);
            _startWatcher.EventArrived += OnProcessStart;
            _startWatcher.Start();

            var stopQuery = new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStopTrace");
            _stopWatcher = new ManagementEventWatcher(stopQuery);
            _stopWatcher.EventArrived += OnProcessStop;
            _stopWatcher.Start();

            return OperationResult.Ok($"Game Detection (WMI): отслеживается {_watched.Count} exe.");
        }
        catch (Exception wmiEx)
        {
            // Fallback polling every 3s
            _pollTimer = new System.Threading.Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            return OperationResult.Ok(
                $"Game Detection (polling fallback): {_watched.Count} exe. WMI: {wmiEx.Message}");
        }
    }

    public void Stop()
    {
        try { _startWatcher?.Stop(); _startWatcher?.Dispose(); } catch { /* ignore */ }
        try { _stopWatcher?.Stop(); _stopWatcher?.Dispose(); } catch { /* ignore */ }
        _startWatcher = null;
        _stopWatcher = null;
        try { _pollTimer?.Dispose(); } catch { /* ignore */ }
        _pollTimer = null;
        lock (_lock) _running.Clear();
    }

    private void OnProcessStart(object sender, EventArrivedEventArgs e)
    {
        try
        {
            string? name = e.NewEvent["ProcessName"]?.ToString();
            if (string.IsNullOrEmpty(name)) return;
            name = Path.GetFileName(name);
            lock (_lock)
            {
                if (!_watched.Contains(name)) return;
                if (!_running.Add(name)) return;
            }
            GameStarted?.Invoke(this, name);
        }
        catch { /* ignore */ }
    }

    private void OnProcessStop(object sender, EventArrivedEventArgs e)
    {
        try
        {
            string? name = e.NewEvent["ProcessName"]?.ToString();
            if (string.IsNullOrEmpty(name)) return;
            name = Path.GetFileName(name);
            lock (_lock)
            {
                if (!_watched.Contains(name)) return;
                if (!_running.Remove(name)) return;
            }
            GameStopped?.Invoke(this, name);
        }
        catch { /* ignore */ }
    }

    private void Poll(object? state)
    {
        try
        {
            HashSet<string> watched;
            lock (_lock) watched = new HashSet<string>(_watched, StringComparer.OrdinalIgnoreCase);

            var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    string n = p.ProcessName + ".exe";
                    if (watched.Contains(n) || watched.Contains(p.ProcessName))
                        now.Add(Path.GetFileName(n));
                }
                catch { /* ignore */ }
                finally { try { p.Dispose(); } catch { /* ignore */ } }
            }

            HashSet<string> started, stopped;
            lock (_lock)
            {
                started = new HashSet<string>(now.Except(_running), StringComparer.OrdinalIgnoreCase);
                stopped = new HashSet<string>(_running.Except(now), StringComparer.OrdinalIgnoreCase);
                _running = now;
            }

            foreach (var s in started) GameStarted?.Invoke(this, s);
            foreach (var s in stopped) GameStopped?.Invoke(this, s);
        }
        catch { /* ignore */ }
    }

    public void Dispose() => Stop();
}
