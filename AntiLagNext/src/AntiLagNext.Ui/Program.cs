using System.Diagnostics;
using System.Text.Json;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Host;
using AntiLagNext.Infrastructure.Storage;
using AntiLagNext.Infrastructure.Tweaks;
using AntiLagNext.Ui.Services;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;

namespace AntiLagNext.Ui;

/// <summary>
/// Photino host. IMPORTANT: SendWebMessage is NOT thread-safe — only call it from
/// Photino's web-message callback (or the UI thread). Never from MonitoringService.
/// </summary>
internal static class Program
{
    private static EngineBootstrap? _engine;
    private static PhotinoWindow? _window;
    private static TrayService? _tray;
    private static SingleInstance? _singleInstance;
    /// <summary>Second instance asked to show before Photino window existed.</summary>
    private static volatile bool _pendingShowFromSecondInstance;
    private static readonly object SendLock = new();
    /// <summary>Serializes engine mutate + BuildUiState (re-entrant Monitor).</summary>
    private static readonly object EngineOpLock = new();
    /// <summary>0 = idle, 1 = apply/revert running on worker.</summary>
    private static int _heavyOpInFlight;
    /// <summary>When true, WindowClosing allows real exit.</summary>
    private static volatile bool _forceExit;
    /// <summary>Started via --autostart (boot): prefer tray, auto-apply.</summary>
    private static bool _autostartMode;
    private static bool _autoApplyStarted;
    /// <summary>
    /// After first paint/uiReady — only then minimize may hide to tray.
    /// Prevents Photino startup minimize events from swallowing the main window.
    /// </summary>
    private static volatile bool _trayHideAllowed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly List<object> LogBuffer = new();
    private static readonly object LogLock = new();

    // Ring buffer: O(1) write, O(n) compact snapshot — no ConcurrentQueue.Count O(n) thrash.
    private static readonly object MetricsLock = new();
    private static readonly double[] LatencyRing = new double[HistoryCap];
    private static int _ringWrite;
    private static int _ringCount;
    private static double _lastMedianUs;
    private static double _lastMaxUs;
    private static double _peak1mUs;
    private static float _cpuPercent;
    // Enough samples for dense charts; wire snapshot is capped separately
    private const int HistoryCap = 480;
    /// <summary>Max points shipped over IPC per metrics reply (keeps JSON small).</summary>
    private const int MetricsWireCap = 160;
    /// <summary>Probe interval (host). UI polls slower — see chartIntervalMs in state.</summary>
    private const int ProbeIntervalMs = 15;
    /// <summary>UI poll interval advertised to the WebView.</summary>
    private const int UiPollIntervalMs = 50;

    /// <summary>
    /// True rolling 1-minute peak: max of per-second buckets (not a growing forever max).
    /// Previous logic reset the window on every new high, so peak only climbed over hours.
    /// </summary>
    private const int PeakWindowSeconds = 60;
    private static readonly double[] PeakSecMax = new double[PeakWindowSeconds];
    private static readonly long[] PeakSecUnix = new long[PeakWindowSeconds];
    /// <summary>Probe glitches above this (µs) are clamped for peak/max display (10 ms).</summary>
    /// <summary>
    /// Probe outliers above this (µs) are treated as measurement glitches.
    /// Must sit above the highest chart Y rung (15 ms) so real spikes remain visible.
    /// </summary>
    private const double ProbeGlitchCapUs = 20_000;

    private static string CrashLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AntiLagNext", "crash.log");

    /// <summary>Localize log/UI strings by Settings.UiCulture (en → English, else Russian).</summary>
    static bool IsEnglishUi() =>
        string.Equals(_engine?.Settings.UiCulture, "en", StringComparison.OrdinalIgnoreCase);

    static string L(string ru, string en) => IsEnglishUi() ? en : ru;

    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        _autostartMode = args.Any(a =>
            a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)
            || a.Equals("/autostart", StringComparison.OrdinalIgnoreCase)
            || a.Equals("-autostart", StringComparison.OrdinalIgnoreCase));

        // Debug / automation escape hatch (not advertised in UI)
        bool allowMulti = args.Any(a =>
            a.Equals("--allow-multi", StringComparison.OrdinalIgnoreCase)
            || a.Equals("/allow-multi", StringComparison.OrdinalIgnoreCase));

        if (args.Length > 0 && args.Any(a =>
                a.StartsWith("--apply", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--revert", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--status", StringComparison.OrdinalIgnoreCase)
                || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("-s", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.Exit(RunSilentCli(args));
                return;
            }
        }

        // Single UI instance — second launch focuses existing window (incl. tray)
        if (!allowMulti)
        {
            _singleInstance = SingleInstance.TryEnter(BringMainWindowToFront);
            if (_singleInstance is null)
            {
                SingleInstance.ActivateExisting();
                return;
            }
        }

        // 1) Engine first (no Photino yet)
        try
        {
            _engine = EngineBootstrap.CreateAsync().GetAwaiter().GetResult();
            _engine.Monitoring.SampleArrived += OnSample;
            // Sync StartWithWindows flag with actual scheduled task
            try
            {
                bool taskOn = StartupRegistration.IsEnabled();
                if (_engine.Settings.StartWithWindows != taskOn)
                {
                    _engine.Settings.StartWithWindows = taskOn;
                    _engine.SettingsService.Save();
                }
            }
            catch { /* ignore */ }
            AddLog(L("Приложение готово", "Ready"), "ok");
        }
        catch (Exception ex)
        {
            WriteCrash("EngineBootstrap", ex);
            MessageBoxNative(L(
                "AntiLag Next не удалось запустить:\n" + ex.Message + "\n\nСм.: " + CrashLogPath,
                "AntiLag Next failed to start engine:\n" + ex.Message + "\n\nSee: " + CrashLogPath));
            return;
        }

        string www = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        if (!File.Exists(www))
        {
            // also try relative to cwd
            www = Path.GetFullPath(Path.Combine("wwwroot", "index.html"));
        }
        if (!File.Exists(www))
        {
            MessageBoxNative(L(
                "Отсутствуют файлы UI:\nwwwroot\\index.html\n\nБаза: " + AppContext.BaseDirectory,
                "UI assets missing:\nwwwroot\\index.html\n\nBase: " + AppContext.BaseDirectory));
            return;
        }

        try
        {
            var window = new PhotinoWindow();
            _window = window;

            string? iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
                window.SetIconFile(iconPath);

            // Launch like the product screenshot: full dashboard window, not forced tray.
            // Size matches min UX (CTA visible); restore after create so we never start minimized.
            window
                .SetTitle("AntiLag Next")
                .SetUseOsDefaultSize(false)
                .SetSize(new System.Drawing.Size(1280, 960))
                .SetMinSize(960, 740)
                .Center()
                .SetResizable(true)
                .SetMinimized(false)
                .RegisterWebMessageReceivedHandler(OnWebMessage)
                .RegisterWindowClosingHandler(OnWindowClosing)
                .RegisterMinimizedHandler((_, _) =>
                {
                    // Only after UI settled — Photino can emit minimize during create
                    if (_trayHideAllowed && _engine?.Settings.MinimizeToTray == true)
                        HideToTray(showBalloon: false);
                })
                .RegisterWindowCreatedHandler((_, _) =>
                {
                    try
                    {
                        // Guarantee visible main window (screenshot look) on normal launch
                        window.SetMinimized(false);
                        IntPtr hwnd = window.WindowHandle;
                        if (hwnd != IntPtr.Zero && (!_autostartMode || _pendingShowFromSecondInstance))
                            TrayService.ShowWindowRestore(hwnd);
                        if (_pendingShowFromSecondInstance)
                        {
                            _pendingShowFromSecondInstance = false;
                            if (hwnd != IntPtr.Zero)
                                TrayService.ShowWindowRestore(hwnd);
                        }
                    }
                    catch { /* ignore */ }
                })
                .Load(new Uri(www, UriKind.Absolute));

            // Verify StartUrl/StartString were set (defensive)
            if (string.IsNullOrWhiteSpace(window.StartUrl) && string.IsNullOrWhiteSpace(window.StartString))
            {
                string html = File.ReadAllText(www);
                window.LoadRawString(html);
                AddLog(L("UI загружен через raw HTML (fallback)", "Loaded UI via raw HTML fallback"), "warn");
            }
            else
            {
                AddLog(L("UI: ", "UI: ") + www, "ok");
            }

            InitTray();

            // --autostart (logon): apply in background, then settle in tray — NOT on interactive launch
            if (_autostartMode)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Let WebView paint once if HWND flashes, then hide
                        await Task.Delay(1200).ConfigureAwait(false);
                        _trayHideAllowed = true;
                        HideToTray(showBalloon: true);
                    }
                    catch { /* ignore */ }
                });
            }

            if (_engine.Settings.MonitoringEnabled)
                AddLog(L("График: старт после UI ready", "Chart: start after UI ready"), "ok");
            else
                AddLog(L("График ВЫКЛ", "Chart OFF"), "ok");

            // Auto-apply only after user previously enabled optimization
            MaybeAutoApplyOnStart();

            window.WaitForClose();
        }
        catch (Exception ex)
        {
            WriteCrash("Photino", ex);
            MessageBoxNative(L(
                "AntiLag Next UI аварийно завершился:\n" + ex.Message +
                "\n\nСм.: " + CrashLogPath +
                "\n\nНужны WebView2 Runtime и .NET 8.",
                "AntiLag Next UI crashed:\n" + ex.Message +
                "\n\nSee: " + CrashLogPath +
                "\n\nNeed WebView2 Runtime + .NET 8."));
        }
        finally
        {
            try
            {
                _tray?.Dispose();
                _tray = null;
                if (_engine != null)
                {
                    _engine.Monitoring.SampleArrived -= OnSample;
                    _engine.Monitoring.Stop();
                }
            }
            catch { /* ignore */ }
            _engine?.Dispose();
            _window = null;
            try { _singleInstance?.Dispose(); } catch { /* ignore */ }
            _singleInstance = null;
        }
    }

    /// <summary>Second-instance signal or tray: restore and focus the Photino window.</summary>
    private static void BringMainWindowToFront()
    {
        try
        {
            var w = _window;
            if (w == null)
            {
                // Engine still starting — focus when Photino is ready
                _pendingShowFromSecondInstance = true;
                SingleInstance.TryFocusWindowByTitle();
                return;
            }

            _pendingShowFromSecondInstance = false;
            w.Invoke(() =>
            {
                try
                {
                    w.SetMinimized(false);
                    var hwnd = w.WindowHandle;
                    if (hwnd != IntPtr.Zero)
                        TrayService.ShowWindowRestore(hwnd);
                }
                catch (Exception ex)
                {
                    WriteCrash("BringMainWindowToFront.Invoke", ex);
                }
            });
            return;
        }
        catch (Exception ex)
        {
            WriteCrash("BringMainWindowToFront", ex);
        }

        SingleInstance.TryFocusWindowByTitle();
    }

    private static void InitTray()
    {
        try
        {
            _tray = new TrayService();
            _tray.Init(L);
            _tray.ShowRequested += () => ShowFromTray();
            _tray.ExitRequested += () => RequestExit();
            _tray.ApplyRequested += () =>
            {
                var profile = ResolveApplyProfileKey();
                QueueHeavyOp(null, isApply: true, profile);
                _tray?.ShowBalloon(
                    L("Оптимизация", "Optimization"),
                    L("Применение профиля…", "Applying profile…"));
            };
            _tray.ResetRequested += () =>
            {
                QueueHeavyOp(null, isApply: false, profile: null);
            };
        }
        catch (Exception ex)
        {
            WriteCrash("InitTray", ex);
        }
    }

    /// <summary>Photino: return true to cancel close.</summary>
    private static bool OnWindowClosing(object sender, EventArgs e)
    {
        if (_forceExit) return false; // allow close
        if (_engine?.Settings.MinimizeToTray == true)
        {
            _trayHideAllowed = true; // user closed window intentionally
            HideToTray(showBalloon: true);
            return true; // cancel
        }
        return false;
    }

    private static void HideToTray(bool showBalloon)
    {
        try
        {
            var w = _window;
            if (w == null) return;
            IntPtr hwnd = w.WindowHandle;
            if (hwnd != IntPtr.Zero)
                TrayService.HideWindow(hwnd);
            else
                w.SetMinimized(true);

            if (showBalloon && _tray != null)
            {
                _tray.ShowBalloon(
                    L("AntiLag Next", "AntiLag Next"),
                    L("Свёрнуто в трей. Двойной клик — открыть.", "Minimized to tray. Double-click to open."));
            }
        }
        catch (Exception ex)
        {
            WriteCrash("HideToTray", ex);
        }
    }

    private static void ShowFromTray()
    {
        try
        {
            // Opening from tray → full dashboard window (product screenshot look)
            _trayHideAllowed = true;
            var w = _window;
            if (w == null) return;
            void restore()
            {
                w.SetMinimized(false);
                w.SetSize(new System.Drawing.Size(1280, 960));
                try { w.Center(); } catch { /* optional */ }
                IntPtr hwnd = w.WindowHandle;
                if (hwnd != IntPtr.Zero)
                    TrayService.ShowWindowRestore(hwnd);
            }
            try { w.Invoke(restore); }
            catch { restore(); }
        }
        catch (Exception ex)
        {
            WriteCrash("ShowFromTray", ex);
        }
    }

    /// <summary>
    /// Full process exit (tray «Exit»). Must allow WindowClosing (_forceExit)
    /// and marshal Close onto the Photino UI thread — tray menu runs on WinForms STA.
    /// </summary>
    private static void RequestExit()
    {
        _forceExit = true;

        // Best-effort: release held timer before teardown (do not throw into tray handler)
        try
        {
            if (_engine?.Settings.ReleaseTimerOnExit == true)
                try { _engine.Timer.Release(); } catch { /* ignore */ }
        }
        catch { /* ignore */ }

        var w = _window;
        if (w == null)
        {
            try { Environment.Exit(0); } catch { /* ignore */ }
            return;
        }

        void doClose()
        {
            try { w.Close(); } catch { /* ignore */ }
        }

        try
        {
            // Tray ContextMenuStrip is not the Photino message loop
            w.Invoke(doClose);
        }
        catch
        {
            try { doClose(); } catch { /* ignore */ }
        }

        // Safety net: if Close is swallowed (race / minimize-to-tray), hard-exit
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500).ConfigureAwait(false);
                if (_forceExit)
                    Environment.Exit(0);
            }
            catch { /* ignore */ }
        });
    }

    private static string ResolveApplyProfileKey()
    {
        var p = _engine?.Settings.GetActiveProfile();
        if (p == null) return "gaming";
        return p.Kind switch
        {
            ProfileKind.Office => "office",
            ProfileKind.MaxPerformance => "max",
            ProfileKind.Gaming => "gaming",
            // Default = «off» — for auto-optimize use gaming
            _ => "gaming"
        };
    }

    private static void MaybeAutoApplyOnStart()
    {
        if (_engine == null || _autoApplyStarted) return;

        bool userOptedIn = _engine.Settings.UserEnabledOptimization;
        bool autoFlag = _engine.Settings.AutoApplyOnStartup;
        // Critical: after Disable/Reset ActiveState is false — must NOT re-apply on next launch
        bool leftOn = ActiveStateStore.IsActive();

        bool want = AutoApplyPolicy.ShouldAutoApplyOnStart(
            userOptedIn, autoFlag, leftOn, _autostartMode);

        if (!want)
        {
            string? skip = AutoApplyPolicy.DescribeSkipReason(
                userOptedIn, autoFlag, leftOn, _autostartMode,
                english: string.Equals(_engine.Settings.UiCulture, "en", StringComparison.OrdinalIgnoreCase));
            if (skip != null)
                AddLog(skip, "ok");
            return;
        }

        _autoApplyStarted = true;

        string profile = ResolveApplyProfileKey();
        string profileLabel = LocalizeProfileLabelFromToken(profile, ProfileKind.Gaming);
        AddLog(L(
            $"Авто-оптимизация при старте («{profileLabel}»)…",
            $"Auto-optimize on start (\"{profileLabel}\")…"), "ok");

        // Delay slightly so window/tray init finishes
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_autostartMode ? 800 : 400).ConfigureAwait(false);
                if (_engine == null) return;

                // Re-check ActiveState after delay (user may have Reset from UI immediately)
                if (!ActiveStateStore.IsActive())
                {
                    AddLog(L(
                        "Авто-оптимизация отменена: состояние уже неактивно.",
                        "Auto-optimize cancelled: state no longer active."), "ok");
                    return;
                }

                OperationResult result;
                lock (EngineOpLock)
                {
                    result = _engine.ApplyAsync(profile).GetAwaiter().GetResult();
                }
                AddLog(result.Success
                        ? L("Авто-оптимизация применена", "Auto-optimize applied")
                          + ": " + LocalizeEngineMessage(result.Message)
                        : L("Авто-оптимизация не удалась", "Auto-optimize failed")
                          + ": " + LocalizeEngineMessage(result.Message),
                    result.Success ? "ok" : "err");
                if (result.Success)
                {
                    _tray?.ShowBalloon(
                        L("Оптимизация включена", "Optimization on"),
                        profileLabel);
                }
            }
            catch (Exception ex)
            {
                WriteCrash("AutoApplyOnStart", ex);
                AddLog(L("Авто-оптимизация: ", "Auto-optimize: ") + ex.Message, "err");
            }
        });
    }

    private static bool ChartIsOn =>
        _engine?.Settings.MonitoringEnabled != false;

    private static void StartChartMonitoring()
    {
        if (_engine == null) return;
        _engine.Settings.MonitoringIntervalMs = ProbeIntervalMs;
        // Stop first to avoid double probe threads (Stop no longer blocks UI thread)
        try { _engine.Monitoring.Stop(); } catch { /* ignore */ }
        _engine.Monitoring.Start(TimeSpan.FromMilliseconds(ProbeIntervalMs));
    }

    private static void StopChartMonitoring()
    {
        try { _engine?.Monitoring.Stop(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Monitoring thread ONLY updates in-memory ring buffer — never touches Photino.
    /// Scalar metrics under MetricsLock; history is ConcurrentQueue.
    /// </summary>
    private static void OnSample(object? sender, MonitoringSample sample)
    {
        if (!ChartIsOn) return;

        try
        {
            lock (MetricsLock)
            {
                double median = SanitizeProbeUs(sample.SchedulingLatencyUs);
                double max = SanitizeProbeUs(sample.SchedulingLatencyMaxUs);
                _lastMedianUs = median;
                _lastMaxUs = max;
                _cpuPercent = sample.CpuUsagePercent;
                _peak1mUs = UpdateRollingPeak1m(max);

                LatencyRing[_ringWrite] = median;
                _ringWrite = (_ringWrite + 1) % HistoryCap;
                if (_ringCount < HistoryCap) _ringCount++;
            }
        }
        catch
        {
            /* never kill probe thread */
        }
    }

    /// <summary>Drop NaN/Inf and clamp absurd waitable-timer glitches.</summary>
    private static double SanitizeProbeUs(double us)
    {
        if (double.IsNaN(us) || double.IsInfinity(us) || us < 0)
            return 0;
        if (us > ProbeGlitchCapUs)
            return ProbeGlitchCapUs;
        return us;
    }

    /// <summary>
    /// Max of samples in the last 60 whole seconds (bucketed). Values older than the window fall out.
    /// </summary>
    private static double UpdateRollingPeak1m(double sampleMaxUs)
    {
        long sec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int idx = (int)(sec % PeakWindowSeconds);
        if (PeakSecUnix[idx] != sec)
        {
            PeakSecUnix[idx] = sec;
            PeakSecMax[idx] = sampleMaxUs;
        }
        else if (sampleMaxUs > PeakSecMax[idx])
        {
            PeakSecMax[idx] = sampleMaxUs;
        }

        long oldest = sec - PeakWindowSeconds + 1;
        double peak = 0;
        for (int i = 0; i < PeakWindowSeconds; i++)
        {
            long t = PeakSecUnix[i];
            if (t >= oldest && t <= sec && PeakSecMax[i] > peak)
                peak = PeakSecMax[i];
        }

        return peak;
    }

    /// <summary>Copy last n samples (oldest→newest). Caller should not hold other locks long.</summary>
    private static double[] SnapshotHistory(int maxPoints)
    {
        lock (MetricsLock)
        {
            int n = Math.Min(_ringCount, Math.Max(0, maxPoints));
            if (n == 0) return Array.Empty<double>();
            var arr = new double[n];
            int start = (_ringWrite - n + HistoryCap) % HistoryCap;
            for (int i = 0; i < n; i++)
                arr[i] = Math.Round(LatencyRing[(start + i) % HistoryCap], 1);
            return arr;
        }
    }

    private static void ClearHistoryUnlocked()
    {
        _ringWrite = 0;
        _ringCount = 0;
        Array.Clear(LatencyRing);
    }

    private readonly struct MetricsSnapshot
    {
        public readonly double MedianUs;
        public readonly double MaxUs;
        public readonly double Peak1mUs;
        public readonly float CpuPercent;

        public MetricsSnapshot(double medianUs, double maxUs, double peak1mUs, float cpuPercent)
        {
            MedianUs = medianUs;
            MaxUs = maxUs;
            Peak1mUs = peak1mUs;
            CpuPercent = cpuPercent;
        }
    }

    private static MetricsSnapshot ReadMetricsSnapshot()
    {
        lock (MetricsLock)
        {
            return new MetricsSnapshot(_lastMedianUs, _lastMaxUs, _peak1mUs, _cpuPercent);
        }
    }

    private static void ClearMetricsUnlocked()
    {
        _lastMedianUs = 0;
        _lastMaxUs = 0;
        _peak1mUs = 0;
        _cpuPercent = 0;
        Array.Clear(PeakSecMax);
        Array.Clear(PeakSecUnix);
    }

    private static int RunSilentCli(string[] args)
    {
        try
        {
            using var engine = EngineBootstrap.CreateAsync().GetAwaiter().GetResult();
            bool apply = args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            bool revert = args.Any(a => a.Equals("--revert", StringComparison.OrdinalIgnoreCase)
                                        || a.Equals("--reset", StringComparison.OrdinalIgnoreCase));
            string? profile = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--apply", StringComparison.OrdinalIgnoreCase))
                    profile = args[i + 1];
            }
            profile ??= "gaming";

            if (apply)
            {
                var r = engine.ApplyAsync(profile).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(r.Message)) Console.WriteLine(r.Message);
                return r.Success ? 0 : 1;
            }
            if (revert)
            {
                var r = engine.RevertAsync().GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(r.Message)) Console.WriteLine(r.Message);
                return r.Success ? 0 : 1;
            }
            if (args.Any(a => a.Equals("--status", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(JsonSerializer.Serialize(engine.BuildStatusSnapshot(), JsonOpts));
                return 0;
            }
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // Cap IPC message size (XSS / buggy JS flood protection)
    private const int MaxWebMessageBytes = 64 * 1024;

    private static void OnWebMessage(object? sender, string message)
    {
        // Runs on Photino's message thread — safe to SendWebMessage here.
        string? id = null;
        try
        {
            if (string.IsNullOrEmpty(message) || message.Length > MaxWebMessageBytes)
            {
                // Best-effort id so UI promise can settle instead of waiting for timeout
                id = TryPeekMessageId(message);
                Reply(id, new { error = "message rejected (empty or too large)" });
                return;
            }

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            string cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
            id = root.TryGetProperty("id", out var i) ? i.GetString() : null;

            // Heavy ops: do NOT block Photino message thread (keeps getMetrics/chart alive).
            // Reply is marshaled back via PhotinoWindow.Invoke.
            if (cmd == "apply")
            {
                string profile = SanitizeProfileToken(
                    root.TryGetProperty("profile", out var p) ? p.GetString() : null);
                QueueHeavyOp(id, isApply: true, profile);
                return;
            }
            if (cmd == "revert")
            {
                QueueHeavyOp(id, isApply: false, profile: null);
                return;
            }
            if (cmd == "reapplyDrift")
            {
                QueueReapplyDrift(id);
                return;
            }
            if (cmd == "fixAudit")
            {
                bool safeOnly = root.TryGetProperty("safeOnly", out var so)
                                && so.ValueKind is JsonValueKind.True or JsonValueKind.False
                                && so.GetBoolean();
                QueueFixAudit(id, safeOnly);
                return;
            }
            if (cmd == "checkUpdate")
            {
                QueueCheckUpdate(id);
                return;
            }
            if (cmd == "startUpdate")
            {
                QueueStartUpdate(id);
                return;
            }
            if (cmd == "fixRecommended")
            {
                // Health one-click: Fix safe findings + reapply drifted (heavy)
                QueueFixRecommended(id);
                return;
            }
            if (cmd == "exportDiagnostics")
            {
                QueueExportDiagnostics(id);
                return;
            }

            // Allowlist commands only (fast path on message thread)
            object payload = cmd switch
            {
                "getState" => BuildUiState(),
                "getDrift" => HandleGetDrift(),
                "getAudit" => HandleGetAudit(),
                "setProfile" => HandleSetProfile(root),
                "setPlugin" => HandleSetPlugin(root),
                "setChart" => HandleSetChart(root),
                "getLogs" => new { logs = SnapshotLogs() },
                "getMetrics" => BuildMetricsOnly(),
                "ping" => new { pong = true, utc = DateTime.UtcNow },
                "uiReady" => HandleUiReady(),
                "openUrl" => HandleOpenUrl(root),
                "setLanguage" => HandleSetLanguage(root),
                "setTheme" => HandleSetTheme(root),
                "setAppOptions" => HandleSetAppOptions(root),
                "hideToTray" => HandleHideToTray(),
                "restartPc" => HandleRestartPc(root),
                "openReleases" => HandleOpenReleases(),
                "completeFirstRun" => HandleCompleteFirstRun(),
                _ => new { error = "unknown cmd" }
            };

            Reply(id, payload);
        }
        catch (Exception ex)
        {
            WriteCrash("OnWebMessage", ex);
            // Always reply with request id when possible — otherwise UI hangs until timeout
            try { Reply(id ?? TryPeekMessageId(message), new { error = "handler error", detail = ex.Message }); }
            catch { /* ignore */ }
        }
    }

    /// <summary>Extract "id" from a raw JSON message without full parse (error path only).</summary>
    private static string? TryPeekMessageId(string? message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > MaxWebMessageBytes)
            return null;
        // Cheap scan: "id":"m123"
        const string marker = "\"id\"";
        int i = message.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        i = message.IndexOf(':', i + marker.Length);
        if (i < 0) return null;
        i = message.IndexOf('"', i + 1);
        if (i < 0) return null;
        int j = message.IndexOf('"', i + 1);
        if (j <= i + 1) return null;
        string id = message.Substring(i + 1, j - i - 1);
        return id.Length is > 0 and <= 64 ? id : null;
    }

    private static object HandleUiReady()
    {
        // Start probe only after WebView is alive — avoids startup freeze
        if (_engine?.Settings.MonitoringEnabled == true)
        {
            try
            {
                StartChartMonitoring();
                AddLog(L("График ВКЛ · зонд 15 мс · UI 50 мс", "Chart ON · probe 15 ms · UI 50 ms"), "ok");
            }
            catch (Exception ex)
            {
                WriteCrash("StartChart.uiReady", ex);
                AddLog(L("Не удалось запустить график: ", "Chart start failed: ") + ex.Message, "err");
            }
        }
        // Refresh tray labels after culture is known
        try { _tray?.RebuildMenu(L); } catch { /* ignore */ }

        // Background update check (non-blocking)
        if (_engine?.Settings.CheckUpdatesOnStartup == true)
            ScheduleStartupUpdateCheck();

        // Allow minimize→tray only after dashboard is up (normal launch stays visible like product UI)
        if (!_autostartMode)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(800).ConfigureAwait(false);
                _trayHideAllowed = true;
                // One more ensure-visible pass after WebView ready
                try
                {
                    var w = _window;
                    if (w == null) return;
                    w.Invoke(() =>
                    {
                        w.SetMinimized(false);
                        var hwnd = w.WindowHandle;
                        if (hwnd != IntPtr.Zero)
                            TrayService.ShowWindowRestore(hwnd);
                    });
                }
                catch { /* ignore */ }
            });
        }

        return new { ok = true, chartEnabled = ChartIsOn, state = BuildUiState() };
    }

    private static object HandleHideToTray()
    {
        HideToTray(showBalloon: true);
        return new { success = true };
    }

    /// <summary>Optional reboot after first optimize — UI must send confirmed:true after dialog.</summary>
    private static object HandleRestartPc(JsonElement root)
    {
        // Require explicit confirm flag so a rogue IPC message cannot reboot silently.
        bool confirmed = root.TryGetProperty("confirmed", out var conf)
                         && conf.ValueKind == JsonValueKind.True;
        if (!confirmed)
        {
            return new
            {
                success = false,
                needsConfirm = true,
                error = "confirm required",
                message = L(
                    "Подтвердите перезагрузку в диалоге.",
                    "Confirm reboot in the dialog.")
            };
        }

        int delaySec = 30;
        if (root.TryGetProperty("delaySec", out var d) && d.TryGetInt32(out int ds))
            delaySec = Math.Clamp(ds, 5, 300);

        try
        {
            // Use System32 path — avoid PATH hijack under elevation
            string shutdown = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "shutdown.exe");
            if (!File.Exists(shutdown))
                shutdown = "shutdown.exe";

            string comment = L(
                "AntiLag Next: перезагрузка для применения части твиков реестра/служб",
                "AntiLag Next: reboot so some registry/service tweaks fully apply");
            var psi = new ProcessStartInfo
            {
                FileName = shutdown,
                Arguments = $"/r /t {delaySec} /c \"{comment.Replace("\"", "'")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
            };
            Process.Start(psi);
            AddLog(L(
                $"Перезагрузка через {delaySec} с…",
                $"Rebooting in {delaySec}s…"), "warn");
            return new { success = true, delaySec };
        }
        catch (Exception ex)
        {
            WriteCrash("RestartPc", ex);
            return new { success = false, error = ex.Message };
        }
    }

    private static object HandleSetAppOptions(JsonElement root)
    {
        if (_engine == null)
            return new { success = false, error = "no engine" };

        bool changed = false;
        if (root.TryGetProperty("minimizeToTray", out var mt) && mt.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _engine.Settings.MinimizeToTray = mt.GetBoolean();
            changed = true;
        }
        if (root.TryGetProperty("autoApplyOnStartup", out var aa) && aa.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _engine.Settings.AutoApplyOnStartup = aa.GetBoolean();
            changed = true;
        }
        if (root.TryGetProperty("startWithWindows", out var sw) && sw.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            bool want = sw.GetBoolean();
            // Enabling requires confirmed=true from UI second dialog (P0)
            bool confirmed = root.TryGetProperty("confirmed", out var conf)
                             && conf.ValueKind == JsonValueKind.True;
            if (want && !confirmed)
            {
                return new
                {
                    success = false,
                    needsConfirm = true,
                    error = "confirm required",
                    message = L(
                        "Подтвердите создание задачи автозапуска (права администратора).",
                        "Confirm creating the autostart task (administrator rights)."),
                    state = BuildUiState()
                };
            }
            if (StartupRegistration.SetEnabled(want, out string msg))
            {
                _engine.Settings.StartWithWindows = want;
                changed = true;
                AddLog(want
                        ? L("Автозапуск Windows: включён", "Start with Windows: on")
                        : L("Автозапуск Windows: выключен", "Start with Windows: off"), "ok");
            }
            else
            {
                AddLog(L("Автозапуск: ", "Autostart: ") + msg, "err");
                return new
                {
                    success = false,
                    error = "autostart failed",
                    message = msg,
                    state = BuildUiState()
                };
            }
        }

        if (changed)
            _engine.SettingsService.Save();

        return new { success = true, state = BuildUiState() };
    }

    private static object HandleSetLanguage(JsonElement root)
    {
        string? lang = root.TryGetProperty("lang", out var l) ? l.GetString() : null;
        if (lang is not ("ru" or "en"))
            return new { success = false, error = "lang must be ru|en" };

        bool changed = !string.Equals(_engine!.Settings.UiCulture, lang, StringComparison.OrdinalIgnoreCase);
        _engine.Settings.UiCulture = lang;
        if (changed)
        {
            _engine.SettingsService.Save();
            try { _tray?.RebuildMenu(L); } catch { /* ignore */ }
            AddLog(L($"Язык: {lang}", $"Language: {lang}"), "ok");
        }
        else
        {
            // Still rebuild tray in case labels were built before culture was known
            try { _tray?.RebuildMenu(L); } catch { /* ignore */ }
        }
        return new { success = true, lang, state = BuildUiState() };
    }

    private static object HandleSetTheme(JsonElement root)
    {
        string? theme = root.TryGetProperty("theme", out var t) ? t.GetString() : null;
        var mapped = theme?.ToLowerInvariant() switch
        {
            "light" => AntiLagNext.Core.Enums.AppTheme.Light,
            "dark" => AntiLagNext.Core.Enums.AppTheme.Dark,
            "system" => AntiLagNext.Core.Enums.AppTheme.System,
            _ => (AntiLagNext.Core.Enums.AppTheme?)null
        };
        if (mapped is null)
            return new { success = false, error = "theme must be dark|light|system" };

        _engine!.Settings.Theme = mapped.Value;
        _engine.SettingsService.Save();
        AddLog(L($"Тема: {mapped}", $"Theme: {mapped}"), "ok");
        return new
        {
            success = true,
            theme = mapped.Value.ToString().ToLowerInvariant(),
            state = BuildUiState()
        };
    }

    /// <summary>Open allowlisted external URLs in default browser (GitHub etc.).</summary>
    private static object HandleOpenUrl(JsonElement root)
    {
        string? url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 512)
            return new { success = false, error = "bad url" };

        // Same policy as release pages — no file:/js:/arbitrary hosts via shell
        if (!AntiLagNext.Infrastructure.Services.UpdateService.IsAllowedReleasePageUrl(url))
            return new { success = false, error = "url not allowed" };

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return new { success = true };
        }
        catch (Exception ex)
        {
            WriteCrash("OpenUrl", ex);
            return new { success = false, error = "open failed" };
        }
    }

    /// <summary>
    /// Run apply/revert off the Photino message thread so chart polling stays responsive.
    /// Single-flight: concurrent heavy ops get a fast "busy" reply.
    /// </summary>
    private static void QueueHeavyOp(string? id, bool isApply, string? profile)
    {
        if (!TryBeginHeavyOp(id))
            return;

        if (isApply)
        {
            string applyLabel = LocalizeProfileLabelFromToken(profile, ProfileKind.Gaming);
            AddLog(L($"Применение профиля «{applyLabel}»…", $"Applying profile \"{applyLabel}\"…"), "ok");
        }
        else
        {
            AddLog(L("Сбросить всё…", "Reset all…"), "warn");
        }

        _ = Task.Run(() =>
        {
            try
            {
                OperationResult result;
                object payload;
                lock (EngineOpLock)
                {
                    if (_engine == null)
                    {
                        payload = new { success = false, message = "engine unavailable", error = "no engine" };
                    }
                    else
                    {
                        result = isApply
                            ? _engine.ApplyAsync(profile).GetAwaiter().GetResult()
                            : _engine.RevertAsync().GetAwaiter().GetResult();

                        string doneLabel = isApply
                            ? LocalizeProfileLabelFromToken(profile, ProfileKind.Gaming)
                            : "";
                        string engineMsg = LocalizeEngineMessage(result.Message);
                        AddLog(isApply
                                ? (result.Success
                                    ? L($"Применён «{doneLabel}»", $"Applied \"{doneLabel}\"")
                                      + (string.IsNullOrWhiteSpace(engineMsg) ? "" : ": " + engineMsg)
                                    : L($"Ошибка применения: {engineMsg}", $"Apply failed: {engineMsg}"))
                                : (result.Success
                                    ? L("Сброс завершён: ", "Reset complete: ") + engineMsg
                                    : L("Сброс не удался: ", "Reset failed: ") + engineMsg),
                            result.Success ? "ok" : "err");

                        bool offerRestart = false;
                        bool offerAutostart = false;
                        bool policiesApplied = false;
                        if (isApply && result.Success)
                        {
                            // Tray + auto-apply flag; autostart only after explicit UI confirm (P0)
                            policiesApplied = ApplyOptimizationLifecyclePolicies(
                                out offerRestart, out offerAutostart);
                        }
                        else if (!isApply && result.Success)
                        {
                            // User turned optimization OFF — do not re-apply on next launch
                            ClearAutoApplyAfterUserDisable();
                        }

                        // Build state while still holding EngineOpLock (re-entrant)
                        payload = new
                        {
                            success = result.Success,
                            message = engineMsg,
                            detail = result.Detail,
                            code = result.Code,
                            offerRestart,
                            offerAutostart,
                            policiesApplied,
                            changeSummary = MapChangeSummary(_engine?.LastApplySummary),
                            state = BuildUiStateCore()
                        };
                    }
                }

                ReplyOnUiThread(id, payload);
            }
            catch (Exception ex)
            {
                WriteCrash(isApply ? "ApplyAsync" : "RevertAsync", ex);
                ReplyOnUiThread(id, new { success = false, error = "handler error", detail = ex.Message });
            }
            finally
            {
                EndHeavyOp();
            }
        });
    }

    private static object? MapChangeSummary(ApplyChangeSummary? s)
    {
        if (s is null) return null;
        return new
        {
            profileKey = s.ProfileKey,
            profileKind = s.ProfileKind,
            appliedUtc = s.AppliedUtc,
            items = s.Items.Select(i => new
            {
                id = i.Id,
                area = i.Area,
                titleKey = i.TitleKey,
                detail = i.Detail,
                risk = i.Risk,
                requiresReboot = i.RequiresReboot
            }).ToList()
        };
    }

    private static object HandleCompleteFirstRun()
    {
        if (_engine == null)
            return new { success = false, error = "no engine" };
        if (!_engine.Settings.FirstRunCompleted)
        {
            _engine.Settings.FirstRunCompleted = true;
            _engine.SettingsService.Save();
        }
        return new { success = true, firstRunCompleted = true, state = BuildUiState() };
    }

    /// <summary>Typed result for audit fix / health ops (avoids reflection on anonymous types).</summary>
    private sealed class FixOpResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public string? Detail { get; init; }
        public int FixedCount { get; init; }
        public object? State { get; init; }
    }

    /// <summary>Health: fix safe audit findings then reapply drifted desired-state.</summary>
    private static void QueueFixRecommended(string? id)
    {
        if (!TryBeginHeavyOp(id))
            return;

        AddLog(L("Здоровье: исправление рекомендуемого…", "Health: applying recommended fixes…"), "ok");

        _ = Task.Run(() =>
        {
            try
            {
                object payload;
                lock (EngineOpLock)
                {
                    if (_engine == null)
                    {
                        payload = new { success = false, message = "engine unavailable", error = "no engine" };
                    }
                    else
                    {
                        // 1) Safe audit fix (own backup session)
                        var audit = RunFixAuditCore(safeOnly: true);

                        // 2) Reapply only owned desired-state (no catalog mass-write).
                        // Skip opening a safety session when there is nothing owned to restore.
                        bool hasDesired = false;
                        try
                        {
                            hasDesired = _engine.Services.GetRequiredService<IDesiredStateStore>()
                                .GetEntries().Count > 0;
                        }
                        catch { /* treat as empty */ }

                        bool driftOk;
                        string driftMsg;
                        if (!hasDesired)
                        {
                            driftOk = true;
                            driftMsg = L(
                                "Дрейф: нет desired-state — пропуск.",
                                "Drift: no desired-state — skipped.");
                        }
                        else
                        {
                            var before = _engine.Safety
                                .BeforeChangesAsync("Health fix recommended (drift)")
                                .GetAwaiter().GetResult();
                            if (!before.Success || before.Value == Guid.Empty)
                            {
                                driftOk = false;
                                driftMsg = LocalizeEngineMessage(before.Message) is { Length: > 0 } bm
                                    ? bm
                                    : L("Не удалось подготовить защиту (drift).", "Could not prepare safety backup (drift).");
                            }
                            else
                            {
                                Guid sessionId = before.Value;
                                var drift = _engine.Drift.ReapplyDriftedAsync(sessionId).GetAwaiter().GetResult();
                                var commit = _engine.Safety.CommitChanges(sessionId);
                                driftOk = drift.Success && commit.Success;
                                driftMsg = LocalizeEngineMessage(drift.Message)
                                           + (commit.Success ? "" : " · " + LocalizeEngineMessage(commit.Message));
                            }
                        }

                        bool ok = audit.Success && driftOk;
                        string msg = (string.IsNullOrWhiteSpace(audit.Message) ? "" : audit.Message + " · ") + driftMsg;
                        AddLog(ok
                                ? L("Рекомендовано: ", "Recommended: ") + msg
                                : L("Рекомендованное исправление: ", "Recommended fix: ") + msg,
                            ok ? "ok" : "err");

                        payload = new
                        {
                            success = ok,
                            message = msg,
                            detail = audit.Detail,
                            fixedCount = audit.FixedCount,
                            state = BuildUiStateCore()
                        };
                    }
                }
                ReplyOnUiThread(id, payload);
            }
            catch (Exception ex)
            {
                WriteCrash("FixRecommended", ex);
                ReplyOnUiThread(id, new { success = false, error = "handler error", detail = ex.Message });
            }
            finally
            {
                EndHeavyOp();
            }
        });
    }

    private static void QueueExportDiagnostics(string? id)
    {
        // Diagnostics is read-only filesystem — do not block other heavy ops hard,
        // but still avoid pile-up if already busy with apply.
        if (!TryBeginHeavyOp(id))
            return;

        _ = Task.Run(() =>
        {
            try
            {
                if (_engine == null)
                {
                    ReplyOnUiThread(id, new { success = false, error = "no engine" });
                    return;
                }

                string logsText;
                lock (LogLock)
                {
                    try
                    {
                        logsText = JsonSerializer.Serialize(LogBuffer, JsonOpts);
                    }
                    catch
                    {
                        logsText = string.Join(Environment.NewLine,
                            LogBuffer.Select(e => e?.ToString() ?? ""));
                    }
                }

                var result = _engine.Diagnostics.ExportZip(logsText);
                if (result.Success && !string.IsNullOrEmpty(result.Value))
                {
                    string path = Path.GetFullPath(result.Value!);
                    // Only open Explorer for files we just wrote under diagnostics dir
                    string diagRoot = Path.GetFullPath(AppPaths.DiagnosticsDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
                    bool pathSafe = path.StartsWith(diagRoot, StringComparison.OrdinalIgnoreCase)
                                    && File.Exists(path)
                                    && path.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) < 0;

                    AddLog(L("Диагностика: ", "Diagnostics: ") + path, "ok");
                    if (pathSafe)
                    {
                        try
                        {
                            string explorer = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                "explorer.exe");
                            if (!File.Exists(explorer)) explorer = "explorer.exe";
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = explorer,
                                Arguments = $"/select,\"{path}\"",
                                UseShellExecute = true
                            });
                        }
                        catch { /* ignore */ }
                    }

                    ReplyOnUiThread(id, new
                    {
                        success = true,
                        path,
                        message = L("Архив диагностики создан", "Diagnostics archive created"),
                        state = BuildUiState()
                    });
                }
                else
                {
                    AddLog(L("Диагностика не удалась: ", "Diagnostics failed: ") + result.Message, "err");
                    ReplyOnUiThread(id, new
                    {
                        success = false,
                        error = result.Message,
                        detail = result.Detail
                    });
                }
            }
            catch (Exception ex)
            {
                WriteCrash("ExportDiagnostics", ex);
                ReplyOnUiThread(id, new { success = false, error = "handler error", detail = ex.Message });
            }
            finally
            {
                EndHeavyOp();
            }
        });
    }

    /// <summary>Single-flight gate for apply/revert/drift/audit mutations.</summary>
    private static bool TryBeginHeavyOp(string? id)
    {
        if (Interlocked.CompareExchange(ref _heavyOpInFlight, 1, 0) != 0)
        {
            Reply(id, new
            {
                success = false,
                busy = true,
                message = L("Операция уже выполняется", "Operation already in progress"),
                error = "busy"
            });
            return false;
        }
        return true;
    }

    /// <summary>Clear heavy-op flag and force next getState to rescan health badges.</summary>
    private static void EndHeavyOp()
    {
        InvalidateHealthSummaryCache();
        Interlocked.Exchange(ref _heavyOpInFlight, 0);
    }

    private static object HandleGetDrift()
    {
        if (_engine == null)
            return new { ok = false, entries = Array.Empty<object>(), driftedCount = 0, error = "no engine" };

        try
        {
            lock (EngineOpLock)
            {
                var entries = _engine.Drift.Scan();
                int drifted = entries.Count(e => e.Status is DriftStatus.Drifted or DriftStatus.Missing);
                return new
                {
                    ok = true,
                    driftedCount = drifted,
                    total = entries.Count,
                    entries = entries.Select(MapDriftEntry).ToList()
                };
            }
        }
        catch (Exception ex)
        {
            WriteCrash("HandleGetDrift", ex);
            return new { ok = false, entries = Array.Empty<object>(), driftedCount = 0, error = ex.Message };
        }
    }

    private static object HandleGetAudit()
    {
        if (_engine == null)
            return new { findings = Array.Empty<object>(), count = 0, error = "no engine" };

        try
        {
            lock (EngineOpLock)
            {
                var findings = _engine.Audit.Scan();
                return new
                {
                    count = findings.Count,
                    findings = findings.Select(MapAuditFinding).ToList()
                };
            }
        }
        catch (Exception ex)
        {
            WriteCrash("HandleGetAudit", ex);
            return new { findings = Array.Empty<object>(), count = 0, error = ex.Message };
        }
    }

    private static object MapDriftEntry(DriftEntry e) => new
    {
        tweakId = e.TweakId,
        status = e.Status.ToString(),
        current = e.Current,
        expected = e.Expected,
        path = e.Path,
        name = e.Name,
        hive = e.Hive
    };

    private static object MapAuditFinding(AuditFinding f) => new
    {
        id = f.Id,
        severity = f.Severity,
        titleKey = f.TitleKey,
        detail = f.Detail,
        suggestedTweakId = f.SuggestedTweakId,
        canFix = f.CanFix,
        area = f.Area
    };

    /// <summary>Re-apply drifted catalog desired-state under a safety backup session.</summary>
    private static void QueueReapplyDrift(string? id)
    {
        if (!TryBeginHeavyOp(id))
            return;

        AddLog(L("Повторное применение drifted-твиков…", "Reapplying drifted tweaks…"), "ok");

        _ = Task.Run(() =>
        {
            try
            {
                object payload;
                lock (EngineOpLock)
                {
                    if (_engine == null)
                    {
                        payload = new { success = false, message = "engine unavailable", error = "no engine" };
                    }
                    else
                    {
                        var before = _engine.Safety
                            .BeforeChangesAsync("Reapply drifted catalog tweaks")
                            .GetAwaiter().GetResult();
                        if (!before.Success || before.Value == Guid.Empty)
                        {
                            payload = new
                            {
                                success = false,
                                message = LocalizeEngineMessage(before.Message) is { Length: > 0 } bm
                                    ? bm
                                    : L("Не удалось подготовить защиту.", "Could not prepare safety backup."),
                                detail = before.Detail,
                                state = BuildUiStateCore()
                            };
                        }
                        else
                        {
                            Guid sessionId = before.Value;
                            var result = _engine.Drift
                                .ReapplyDriftedAsync(sessionId)
                                .GetAwaiter().GetResult();
                            var commit = _engine.Safety.CommitChanges(sessionId);
                            bool ok = result.Success && commit.Success;
                            string msg = LocalizeEngineMessage(result.Message)
                                         + (commit.Success ? "" : " · " + LocalizeEngineMessage(commit.Message));
                            AddLog(ok
                                    ? L("Drift reapply: ", "Drift reapply: ") + msg
                                    : L("Drift reapply не удался: ", "Drift reapply failed: ")
                                      + msg,
                                ok ? "ok" : "err");
                            payload = new
                            {
                                success = ok,
                                message = msg,
                                detail = result.Detail ?? commit.Detail,
                                state = BuildUiStateCore()
                            };
                        }
                    }
                }
                ReplyOnUiThread(id, payload);
            }
            catch (Exception ex)
            {
                WriteCrash("ReapplyDrift", ex);
                ReplyOnUiThread(id, new { success = false, error = "handler error", detail = ex.Message });
            }
            finally
            {
                EndHeavyOp();
            }
        });
    }

    /// <summary>
    /// Apply catalog tweaks for audit findings with <see cref="AuditFinding.CanFix"/>.
    /// When <paramref name="safeOnly"/> is true, only <see cref="TweakRisk.Safe"/> tweaks are applied.
    /// </summary>
    private static void QueueFixAudit(string? id, bool safeOnly)
    {
        if (!TryBeginHeavyOp(id))
            return;

        AddLog(safeOnly
                ? L("Аудит: исправление безопасных…", "Audit: fixing safe findings…")
                : L("Аудит: исправление всех…", "Audit: fixing all findings…"), "ok");

        _ = Task.Run(() =>
        {
            try
            {
                object payload;
                lock (EngineOpLock)
                {
                    if (_engine == null)
                    {
                        payload = new { success = false, message = "engine unavailable", error = "no engine" };
                    }
                    else
                    {
                        var fix = RunFixAuditCore(safeOnly);
                        payload = new
                        {
                            success = fix.Success,
                            message = fix.Message,
                            detail = fix.Detail,
                            fixedCount = fix.FixedCount,
                            state = fix.State ?? BuildUiStateCore()
                        };
                    }
                }
                ReplyOnUiThread(id, payload);
            }
            catch (Exception ex)
            {
                WriteCrash("FixAudit", ex);
                ReplyOnUiThread(id, new { success = false, error = "handler error", detail = ex.Message });
            }
            finally
            {
                EndHeavyOp();
            }
        });
    }

    /// <summary>Caller must hold EngineOpLock.</summary>
    private static FixOpResult RunFixAuditCore(bool safeOnly)
    {
        var findings = _engine!.Audit.Scan()
            .Where(f => f.CanFix && !string.IsNullOrWhiteSpace(f.SuggestedTweakId))
            .ToList();

        var defs = new List<TweakDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in findings)
        {
            string tid = f.SuggestedTweakId!;
            if (!seen.Add(tid)) continue;
            var def = TweakCatalog.GetById(tid);
            if (def is null) continue;
            if (safeOnly && def.Risk != TweakRisk.Safe) continue;
            defs.Add(def);
        }

        if (defs.Count == 0)
        {
            string emptyMsg = L(
                "Аудит: нечего исправлять (или нет Safe-твиков).",
                "Audit: nothing to fix (or no Safe tweaks).");
            AddLog(emptyMsg, "ok");
            return new FixOpResult
            {
                Success = true,
                Message = emptyMsg,
                FixedCount = 0,
                State = BuildUiStateCore()
            };
        }

        var before = _engine.Safety
            .BeforeChangesAsync(safeOnly ? "Audit fix (safe)" : "Audit fix (all)")
            .GetAwaiter().GetResult();
        if (!before.Success || before.Value == Guid.Empty)
        {
            return new FixOpResult
            {
                Success = false,
                Message = LocalizeEngineMessage(before.Message) is { Length: > 0 } bm
                    ? bm
                    : L("Не удалось подготовить защиту.", "Could not prepare safety backup."),
                Detail = before.Detail,
                State = BuildUiStateCore()
            };
        }

        Guid sessionId = before.Value;
        var engine = _engine.Services.GetRequiredService<RegistryTweakEngine>();
        var result = engine.ApplyAsync(defs, sessionId).GetAwaiter().GetResult();
        var commit = _engine.Safety.CommitChanges(sessionId);
        bool ok = result.Success && commit.Success;
        string msg = LocalizeEngineMessage(result.Message)
                     + (commit.Success ? "" : " · " + LocalizeEngineMessage(commit.Message));
        // Surface write-level detail in logs (path/type/access) — was truncated to Message only
        if (!string.IsNullOrWhiteSpace(result.Detail))
            msg += " · " + result.Detail;
        AddLog(ok
                ? L($"Аудит: исправлено {defs.Count}", $"Audit: fixed {defs.Count}")
                  + (string.IsNullOrWhiteSpace(msg) ? "" : ": " + LocalizeEngineMessage(msg))
                : L("Аудит: ошибка: ", "Audit fix failed: ") + LocalizeEngineMessage(msg),
            ok ? "ok" : "err");

        return new FixOpResult
        {
            Success = ok,
            Message = msg,
            Detail = result.Detail ?? commit.Detail,
            FixedCount = defs.Count,
            State = BuildUiStateCore()
        };
    }

    /// <summary>Marshal Reply onto Photino UI thread (required for SendWebMessage).</summary>
    private static void ReplyOnUiThread(string? id, object payload)
    {
        var window = _window;
        if (window == null) return;
        try
        {
            window.Invoke(() => Reply(id, payload));
        }
        catch (Exception ex)
        {
            WriteCrash("ReplyOnUiThread.Invoke", ex);
            // Last resort — locked send (may still work on some Photino builds)
            try { Reply(id, payload); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// After user enables optimization: tray + allow auto-apply on later starts
    /// (only while ActiveState remains ON — see <see cref="AutoApplyPolicy"/>).
    /// Autostart (schtasks) is NOT created here — UI must confirm (P0).
    /// </summary>
    private static bool ApplyOptimizationLifecyclePolicies(out bool offerRestart, out bool offerAutostart)
    {
        offerRestart = true; // registry/network/service tweaks benefit from reboot; timer/power already live
        offerAutostart = false;
        if (_engine == null) return false;

        _engine.Settings.UserEnabledOptimization = true;
        _engine.Settings.MinimizeToTray = true;
        // Prefer re-apply after reboot / next launch only while user leaves optimization ON
        _engine.Settings.AutoApplyOnStartup = true;
        _engine.Settings.FirstRunCompleted = true;

        // Offer autostart only if not already registered
        offerAutostart = !_engine.Settings.StartWithWindows || !StartupRegistration.IsEnabled();

        _engine.SettingsService.Save();
        AddLog(L(
            "После включения: трей + авто-оптимизация при следующих запусках, если оставить включённой (автозапуск Windows — по подтверждению)",
            "After enable: tray + auto-optimize on later launches if left on (Windows autostart requires confirm)"), "ok");
        return true;
    }

    /// <summary>
    /// After Reset / Disable: clear auto-apply preference so the next cold start
    /// does not re-enable optimization without an explicit user Enable.
    /// Keeps <see cref="AppSettings.UserEnabledOptimization"/> so Settings still show prior use.
    /// </summary>
    private static void ClearAutoApplyAfterUserDisable()
    {
        if (_engine == null) return;
        if (!_engine.Settings.AutoApplyOnStartup) return;
        _engine.Settings.AutoApplyOnStartup = false;
        _engine.SettingsService.Save();
        AddLog(L(
            "Авто-оптимизация при старте выключена (оптимизация сброшена пользователем).",
            "Optimize-on-startup turned off (user disabled optimization)."), "ok");
    }

    private static string SanitizeProfileToken(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return "gaming";
        string s = profile.Trim();
        if (s.Length > 64) s = s[..64];
        // Drop control chars / path junk from adversarial UI messages
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char ch in s)
        {
            if (char.IsControl(ch) || ch is '/' or '\\' or ':' or '"' or '<' or '>')
                continue;
            buf[n++] = ch;
        }
        return n == 0 ? "gaming" : new string(buf[..n]);
    }

    private static object HandleSetProfile(JsonElement root)
    {
        string profile = root.TryGetProperty("profile", out var p)
            ? p.GetString() ?? "gaming"
            : "gaming";
        profile = SanitizeProfileToken(profile);
        var resolved = _engine!.ResolveProfile(profile);
        _engine.Settings.ActiveProfileId = resolved.Id;
        if (!_engine.Settings.Profiles.Any(x => x.Id == resolved.Id))
            _engine.Settings.Profiles.Add(resolved);
        _engine.SettingsService.Save();
        string key = MapKindToUiId(resolved.Kind);
        string label = LocalizeProfileLabel(resolved.Kind);
        AddLog(L($"Выбран профиль: {label}", $"Profile selected: {label}"), "ok");
        return new
        {
            success = true,
            profile = label,
            profileKey = key,
            kind = resolved.Kind.ToString(),
            state = BuildUiState()
        };
    }

    private static object HandleSetPlugin(JsonElement root)
    {
        string pluginId = root.TryGetProperty("pluginId", out var pid) ? pid.GetString() ?? "" : "";
        bool enabled = root.TryGetProperty("enabled", out var en) && en.GetBoolean();
        var plugin = _engine!.Plugins.GetById(pluginId);
        if (plugin == null)
            return new { success = false, message = "Plugin not found: " + pluginId };

        // P0: experimental stubs cannot be toggled on — no false sense of effect
        if (plugin.Category == AntiLagNext.Core.Plugins.PluginCategory.Experimental)
        {
            plugin.IsEnabled = false;
            _engine.Settings.PluginEnabled[pluginId] = false;
            _engine.SettingsService.Save();
            return new
            {
                success = false,
                stub = true,
                message = L(
                    "Экспериментальный модуль — заглушка MVP, система не меняется.",
                    "Experimental module is an MVP stub; the system is not changed."),
                state = BuildUiState()
            };
        }

        plugin.IsEnabled = enabled;
        _engine.Settings.PluginEnabled[pluginId] = enabled;
        _engine.SettingsService.Save();
        AddLog(L(
            $"Плагин {pluginId} = {(enabled ? "вкл" : "выкл")}",
            $"Plugin {pluginId} = {(enabled ? "on" : "off")}"), "ok");
        return new { success = true, state = BuildUiState() };
    }

    /// <summary>
    /// Toggle latency chart probe. Must stay off the heavy path:
    /// no EngineOpLock / full BuildUiState (that stalls getMetrics 1–2s+ under apply).
    /// Persist settings asynchronously so the Photino message thread stays free.
    /// </summary>
    private static object HandleSetChart(JsonElement root)
    {
        bool enabled = root.TryGetProperty("enabled", out var en) && en.GetBoolean();
        var engine = _engine!;
        engine.Settings.MonitoringEnabled = enabled;
        engine.Settings.MonitoringIntervalMs = ProbeIntervalMs;

        // Disk I/O off message thread — serial Save under AV can freeze chart IPC for seconds
        _ = Task.Run(() =>
        {
            try { engine.SettingsService.Save(); }
            catch { /* best-effort */ }
        });

        if (enabled)
        {
            StartChartMonitoring();
            AddLog(L("График ВКЛ · зонд 15 мс · UI 50 мс", "Chart ON · probe 15 ms · UI 50 ms"), "ok");
        }
        else
        {
            StopChartMonitoring();
            lock (MetricsLock)
            {
                ClearMetricsUnlocked();
                ClearHistoryUnlocked();
            }
            AddLog(L("График ВЫКЛ · зонд остановлен", "Chart OFF · probe stopped"), "ok");
        }

        // Slim reply only — UI already flips optimistically; full getState would re-block IPC
        return new
        {
            success = true,
            chartEnabled = enabled,
            message = enabled ? "Chart enabled" : "Chart disabled"
        };
    }

    /// <summary>
    /// Compact metrics for high-frequency poll. Full history only every Nth request
    /// to avoid 200×/s large JSON allocations over WebView IPC.
    /// </summary>
    private static int _metricsPollCount;

    /// <summary>UI soft-cap label (same order of magnitude as probe glitch clamp).</summary>
    /// <summary>Display/wire peak ceiling — matches top fixed Y scale (15_000 µs).</summary>
    private const double PeakDisplayCapUs = 15_000;

    private static object BuildMetricsOnly()
    {
        int n = Interlocked.Increment(ref _metricsPollCount);
        var snap = ReadMetricsSnapshot();
        // Ship a compact history every poll (UI is 50ms) — cheap ring copy
        bool withHistory = (n & 1) == 0;
        // Peak is already rolling 60s + glitch-clamped; display cap is secondary soft limit
        double peakShow = Math.Min(snap.Peak1mUs, PeakDisplayCapUs);
        double maxShow = Math.Min(snap.MaxUs, PeakDisplayCapUs);
        return new
        {
            v = Math.Round(snap.MedianUs, 1),
            m = Math.Round(maxShow, 1),
            p = Math.Round(peakShow, 1),
            peakClamped = snap.Peak1mUs > PeakDisplayCapUs,
            t = Math.Round(_engine?.Timer.CurrentState.ActualMs ?? 0, 3),
            c = Math.Round(snap.CpuPercent, 1),
            history = withHistory ? SnapshotHistory(MetricsWireCap) : null
        };
    }

    /// <summary>
    /// UI snapshot. Must not block the Photino message loop for long:
    /// if a heavy apply holds <see cref="EngineOpLock"/>, return a lightweight busy
    /// payload immediately so getState cannot time out → «Нет ответа приложения».
    /// </summary>
    private static object BuildUiState()
    {
        bool taken = false;
        try
        {
            // ~300ms: long enough for a quick critical section, short enough to keep IPC alive
            // Fully-qualified: Photino.NET also has a Monitor type
            System.Threading.Monitor.TryEnter(EngineOpLock, 300, ref taken);
            if (!taken)
                return BuildUiStateHostBusy();
            return BuildUiStateCore();
        }
        finally
        {
            if (taken)
                System.Threading.Monitor.Exit(EngineOpLock);
        }
    }

    /// <summary>
    /// Minimal getState while apply/revert holds the engine lock.
    /// Keeps WebView promises resolving so the status bar does not show a false timeout.
    /// </summary>
    private static object BuildUiStateHostBusy()
    {
        try
        {
            var active = ActiveStateStore.Load();
            var profile = _engine?.Settings.GetActiveProfile();
            string profileKey = profile != null ? MapKindToUiId(profile.Kind) : "gaming";
            string culture = string.IsNullOrWhiteSpace(_engine?.Settings.UiCulture)
                ? "ru"
                : _engine!.Settings.UiCulture.ToLowerInvariant();
            if (culture is not ("ru" or "en")) culture = "ru";

            return new
            {
                optimized = active.Active,
                profileKey,
                selectedProfileId = profileKey,
                profileKind = profile?.Kind.ToString(),
                lang = culture,
                chartEnabled = ChartIsOn,
                busy = true,
                hostBusy = true,
                appVersion = _engine?.Update.LocalVersion,
                // Cached health only — never full Scan() while locked out
                drift = _cachedDriftSummary ?? new { driftedCount = 0, total = 0 },
                audit = _cachedAuditSummary ?? new { issueCount = 0 }
            };
        }
        catch
        {
            return new
            {
                busy = true,
                hostBusy = true,
                chartEnabled = ChartIsOn
            };
        }
    }

    /// <summary>Caller must hold EngineOpLock (or be single-threaded startup).</summary>
    private static object BuildUiStateCore()
    {
        var active = ActiveStateStore.Load();
        var timer = _engine!.Timer.CurrentState;
        var profile = _engine.Settings.GetActiveProfile();
        var metrics = ReadMetricsSnapshot();

        double latency = metrics.MedianUs > 0
            ? metrics.MedianUs
            : (timer.MeasuredJitterUs > 0 ? timer.MeasuredJitterUs : 0);
        double peakRaw = metrics.Peak1mUs > 0 ? metrics.Peak1mUs : metrics.MaxUs;
        double peak = Math.Min(peakRaw, PeakDisplayCapUs);

        bool chartOn = _engine.Settings.MonitoringEnabled;

        string culture = string.IsNullOrWhiteSpace(_engine.Settings.UiCulture)
            ? "ru"
            : _engine.Settings.UiCulture.ToLowerInvariant();
        if (culture is not ("ru" or "en")) culture = "ru";

        // Map system → concrete light/dark for WebView (resolves OS preference once per state)
        string theme = _engine.Settings.Theme switch
        {
            AntiLagNext.Core.Enums.AppTheme.Light => "light",
            AntiLagNext.Core.Enums.AppTheme.System => OsPrefersLightTheme() ? "light" : "dark",
            _ => "dark"
        };

        // ALWAYS localize from Kind / ActiveProfile — never from stored Russian display names
        // (active-state.json may still contain legacy "Игровой")
        string profileKey = MapKindToUiId(profile.Kind);
        string profileLabel = LocalizeProfileLabel(profile.Kind);

        // Heal legacy ActiveState display names → stable key (gaming/office/max)
        try
        {
            if (active.Active
                && !string.IsNullOrEmpty(active.ProfileName)
                && !string.Equals(active.ProfileName, profileKey, StringComparison.OrdinalIgnoreCase)
                && (active.ProfileName.IndexOfAny(new[] { 'А', 'а', 'И', 'и', 'О', 'о', 'П', 'п', 'М', 'м' }) >= 0
                    || active.ProfileName.Contains("Игровой", StringComparison.OrdinalIgnoreCase)
                    || active.ProfileName.Contains("Офис", StringComparison.OrdinalIgnoreCase)
                    || active.ProfileName.Contains("Максим", StringComparison.OrdinalIgnoreCase)
                    || active.ProfileName.Contains("умолчан", StringComparison.OrdinalIgnoreCase)))
            {
                ActiveStateStore.MarkActive(profileKey);
            }
        }
        catch { /* best-effort heal */ }

        return new
        {
            optimized = active.Active,
            // Display helpers for both UI languages (card picks by RU/EN toggle)
            profile = profileLabel,
            profileEn = OptimizationProfile.LocalizedName(profile.Kind, "en"),
            profileRu = OptimizationProfile.LocalizedName(profile.Kind, "ru"),
            profileKey,
            profileKind = profile.Kind.ToString(),
            selectedProfileId = profileKey,
            lang = culture,
            theme,
            chartEnabled = chartOn,
            chartIntervalMs = UiPollIntervalMs,
            probeIntervalMs = ProbeIntervalMs,
            timerMs = timer.IsActive ? timer.ActualMs : 0,
            timerHeld = timer.IsActive,
            latencyUs = chartOn && latency > 0 ? Math.Round(latency, 1) : (double?)null,
            peakUs = chartOn && peak > 0 ? Math.Round(peak, 1) : (double?)null,
            maxUs = chartOn && metrics.MaxUs > 0 ? Math.Round(metrics.MaxUs, 1) : (double?)null,
            cpu = Math.Round(metrics.CpuPercent, 1),
            // Never attach full history to getState — that JSON freezes startup WebView
            history = Array.Empty<double>(),
            busy = Volatile.Read(ref _heavyOpInFlight) != 0,
            minimizeToTray = _engine.Settings.MinimizeToTray,
            startWithWindows = _engine.Settings.StartWithWindows,
            autoApplyOnStartup = _engine.Settings.AutoApplyOnStartup,
            userEnabledOptimization = _engine.Settings.UserEnabledOptimization,
            autostartMode = _autostartMode,
            plugins = _engine.Plugins.Plugins.Select(p =>
            {
                bool supported = p.IsSupported(out var reason);
                var st = p.GetStatus();
                bool experimental = p.Category == AntiLagNext.Core.Plugins.PluginCategory.Experimental;
                // MVP stubs must not look like working toggles (P0)
                bool stub = experimental
                            || (st.Message?.Contains("stub", StringComparison.OrdinalIgnoreCase) == true)
                            || (st.Message?.Contains("not applied", StringComparison.OrdinalIgnoreCase) == true)
                            || (st.Message?.Contains("MVP", StringComparison.OrdinalIgnoreCase) == true
                                && experimental);
                return new
                {
                    id = p.Id,
                    enabled = stub ? false : p.IsEnabled,
                    appliedByCore = p.AppliedByCore,
                    category = p.Category.ToString(),
                    experimental,
                    stub,
                    supported = stub ? false : supported,
                    reason = stub
                        ? L("Заглушка MVP — не изменяет систему", "MVP stub — does not change the system")
                        : reason,
                    state = st.State.ToString(),
                    message = st.Message
                };
            }).ToList(),
            logs = SnapshotLogs(),
            // Compact health summary for dashboard badges (no full entry lists)
            drift = BuildDriftSummary(),
            audit = BuildAuditSummary(),
            firstRunCompleted = _engine.Settings.FirstRunCompleted,
            changeSummary = MapChangeSummary(_engine.LastApplySummary),
            appVersion = _engine.Update.LocalVersion,
            update = _lastUpdateCheck is null ? null : new
            {
                hasUpdate = _lastUpdateCheck.HasUpdate,
                local = _lastUpdateCheck.LocalVersion,
                latest = _lastUpdateCheck.LatestVersion,
                canSilent = _lastUpdateCheck.CanSilentInstall,
                portable = _lastUpdateCheck.IsPortable,
                releaseUrl = _lastUpdateCheck.ReleaseUrl,
                // Only non-empty when check actually failed (never fabricate on success)
                error = string.IsNullOrEmpty(_lastUpdateCheck.Error)
                        && string.IsNullOrEmpty(_lastUpdateCheck.ErrorCode)
                    ? null
                    : NullIfEmpty(LocalizeUpdateError(_lastUpdateCheck)),
                errorCode = string.IsNullOrEmpty(_lastUpdateCheck.ErrorCode)
                    ? null
                    : _lastUpdateCheck.ErrorCode
            }
        };
    }

    private static UpdateCheckResult? _lastUpdateCheck;

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// UI/log message for update errors — respects UiCulture (never OS-locale).
    /// Empty string when the check succeeded (no Error / ErrorCode).
    /// </summary>
    private static string LocalizeUpdateError(UpdateCheckResult? r)
    {
        if (r is null) return "";
        // Success path: never invent a network error for the settings line
        if (string.IsNullOrEmpty(r.Error) && string.IsNullOrEmpty(r.ErrorCode))
            return "";

        string code = r.ErrorCode ?? "";
        return code switch
        {
            "timeout" => L(
                "Проверка обновлений превысила время ожидания. Попробуйте снова или откройте Releases.",
                "Update check timed out. Try again or open Releases."),
            "network" => L(
                "Не удалось связаться с GitHub. Проверьте сеть или откройте Releases вручную.",
                "Could not reach GitHub. Check network or open Releases manually."),
            "http" => L(
                "GitHub вернул ошибку. Откройте Releases вручную.",
                "GitHub returned an error. Open Releases manually."),
            "parse" => L(
                "Не удалось разобрать версию релиза.",
                "Could not parse release version."),
            "unknown" => L(
                "Не удалось связаться с GitHub. Проверьте сеть или откройте Releases вручную.",
                "Could not reach GitHub. Check network or open Releases manually."),
            _ when !string.IsNullOrEmpty(r.Error) =>
                L(MapUpdateErrorToRu(r.Error), r.Error!),
            _ => L(
                "Не удалось связаться с GitHub. Проверьте сеть или откройте Releases вручную.",
                "Could not reach GitHub. Check network or open Releases manually.")
        };
    }

    private static string MapUpdateErrorToRu(string en) => en switch
    {
        "Could not reach GitHub. Check network or open Releases manually." =>
            "Не удалось связаться с GitHub. Проверьте сеть или откройте Releases вручную.",
        "Update check timed out. Try again or open Releases." =>
            "Проверка обновлений превысила время ожидания. Попробуйте снова или откройте Releases.",
        "Could not parse version" => "Не удалось разобрать версию релиза.",
        "Update check failed. Open Releases for the latest Setup." =>
            "Проверка обновлений не удалась. Откройте Releases.",
        _ => en // already EN or unknown — do not invent Russian from Win32
    };

    private static void ScheduleStartupUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000).ConfigureAwait(false);
                if (_engine == null) return;
                // Throttle: at most once per 6 hours
                var last = _engine.Settings.LastUpdateCheckUtc;
                if (last is DateTime t && DateTime.UtcNow - t < TimeSpan.FromHours(6))
                    return;
                var result = await _engine.Update.CheckAsync().ConfigureAwait(false);
                _lastUpdateCheck = result;
                // Only stamp throttle on success (no error) so flaky network retries next launch
                if (string.IsNullOrEmpty(result.Error))
                {
                    _engine.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                    try { _engine.SettingsService.Save(); } catch { /* ignore */ }
                }
                if (result.HasUpdate)
                {
                    AddLog(L(
                        $"Доступно обновление {result.LatestVersion} (сейчас {result.LocalVersion})",
                        $"Update available {result.LatestVersion} (now {result.LocalVersion})"), "ok");
                }
                else if (!string.IsNullOrEmpty(result.Error))
                {
                    AddLog(L("Обновление: ", "Update: ") + LocalizeUpdateError(result), "err");
                }
            }
            catch (Exception ex)
            {
                WriteCrash("StartupUpdateCheck", ex);
            }
        });
    }

    private static void QueueCheckUpdate(string? id)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_engine == null)
                {
                    ReplyOnUiThread(id, new { success = false, error = "no engine" });
                    return;
                }
                AddLog(L("Проверка обновлений…", "Checking for updates…"), "ok");
                var result = await _engine.Update.CheckAsync().ConfigureAwait(false);
                _lastUpdateCheck = result;
                if (string.IsNullOrEmpty(result.Error))
                {
                    _engine.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                    try { _engine.SettingsService.Save(); } catch { /* ignore */ }
                }

                string? errMsg = string.IsNullOrEmpty(result.Error) ? null : LocalizeUpdateError(result);
                // Breadcrumb from UpdateService (atom/api stage tags) — logs only
                if (!string.IsNullOrEmpty(result.ReleaseNotes)
                    && result.ReleaseNotes.Length < 120
                    && result.ReleaseNotes.Contains(':', StringComparison.Ordinal))
                    AddLog("Update stages: " + result.ReleaseNotes, string.IsNullOrEmpty(result.Error) ? "ok" : "err");

                // Never let BuildUiState crash turn a good check into a fake failure
                object? state = null;
                try { state = BuildUiState(); }
                catch (Exception stEx)
                {
                    WriteCrash("CheckUpdate.BuildUiState", stEx);
                }

                if (!string.IsNullOrEmpty(result.Error) && !result.HasUpdate)
                {
                    AddLog(L("Обновление: ", "Update: ") + errMsg, "err");
                    ReplyOnUiThread(id, new
                    {
                        success = false,
                        error = errMsg,
                        errorCode = result.ErrorCode,
                        local = result.LocalVersion,
                        latest = result.LatestVersion,
                        releaseUrl = result.ReleaseUrl,
                        state
                    });
                    return;
                }

                AddLog(result.HasUpdate
                        ? L($"Доступна {result.LatestVersion}", $"Available {result.LatestVersion}")
                        : L("Уже актуальная версия", "Already up to date"),
                    "ok");
                ReplyOnUiThread(id, new
                {
                    success = true,
                    hasUpdate = result.HasUpdate,
                    local = result.LocalVersion,
                    latest = result.LatestVersion,
                    canSilent = result.CanSilentInstall,
                    portable = result.IsPortable,
                    releaseUrl = result.ReleaseUrl,
                    downloadUrl = result.DownloadUrl,
                    state
                });
            }
            catch (Exception ex)
            {
                WriteCrash("CheckUpdate", ex);
                var (code, en) = AntiLagNext.Infrastructure.Services.UpdateService.ClassifyError(ex);
                var fake = new UpdateCheckResult
                {
                    LocalVersion = _engine?.Update.LocalVersion ?? "0.0.0",
                    Error = en,
                    ErrorCode = code
                };
                ReplyOnUiThread(id, new
                {
                    success = false,
                    error = LocalizeUpdateError(fake),
                    errorCode = code,
                    local = fake.LocalVersion
                });
            }
        });
    }

    private static void QueueStartUpdate(string? id)
    {
        if (!TryBeginHeavyOp(id))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (_engine == null)
                {
                    ReplyOnUiThread(id, new { success = false, error = "no engine" });
                    return;
                }

                var check = _lastUpdateCheck;
                if (check is null || !check.HasUpdate)
                    check = await _engine.Update.CheckAsync().ConfigureAwait(false);
                _lastUpdateCheck = check;

                if (!check.HasUpdate)
                {
                    ReplyOnUiThread(id, new
                    {
                        success = true,
                        hasUpdate = false,
                        message = L("Уже актуальная версия", "Already up to date"),
                        state = BuildUiState()
                    });
                    return;
                }

                if (!check.CanSilentInstall)
                {
                    ReplyOnUiThread(id, new
                    {
                        success = false,
                        needsBrowser = true,
                        releaseUrl = check.ReleaseUrl ?? AntiLagNext.Infrastructure.Services.UpdateService.ReleasesPage,
                        message = L(
                            "Тихая установка только из Program Files. Откройте страницу Releases.",
                            "Silent install requires Program Files install. Open Releases page."),
                        state = BuildUiState()
                    });
                    return;
                }

                AddLog(L(
                    $"Скачивание {check.LatestVersion}…",
                    $"Downloading {check.LatestVersion}…"), "ok");

                var result = await _engine.Update.DownloadAndInstallAsync(check).ConfigureAwait(false);
                if (!result.Success)
                {
                    AddLog(L("Обновление не удалось: ", "Update failed: ") + result.Message, "err");
                    ReplyOnUiThread(id, new
                    {
                        success = false,
                        error = result.Message,
                        detail = result.Detail,
                        releaseUrl = check.ReleaseUrl,
                        state = BuildUiState()
                    });
                    return;
                }

                AddLog(L("Установщик запущен — выход…", "Setup started — exiting…"), "ok");
                ReplyOnUiThread(id, new { success = true, exiting = true, message = result.Message });

                // Exit so Inno can replace files
                await Task.Delay(500).ConfigureAwait(false);
                try { _forceExit = true; } catch { /* ignore */ }
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                WriteCrash("StartUpdate", ex);
                var (code, en) = AntiLagNext.Infrastructure.Services.UpdateService.ClassifyError(ex);
                var fake = new UpdateCheckResult
                {
                    LocalVersion = _engine?.Update.LocalVersion ?? "0.0.0",
                    Error = en,
                    ErrorCode = code
                };
                ReplyOnUiThread(id, new
                {
                    success = false,
                    error = LocalizeUpdateError(fake),
                    errorCode = code
                });
            }
            finally
            {
                EndHeavyOp();
            }
        });
    }

    private static object HandleOpenReleases()
    {
        try
        {
            string url = _lastUpdateCheck?.ReleaseUrl
                         ?? AntiLagNext.Infrastructure.Services.UpdateService.ReleasesPage;
            // Shell-open only allowlisted GitHub release pages (no file:/javascript:/etc.)
            if (!AntiLagNext.Infrastructure.Services.UpdateService.IsAllowedReleasePageUrl(url))
                url = AntiLagNext.Infrastructure.Services.UpdateService.ReleasesPage;

            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return new { success = true, url };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// Drift/audit full registry scans are expensive — never run them on every getState (8s poll).
    /// Cached results keep dashboard badges fresh enough without freezing the Photino thread.
    /// </summary>
    private static object? _cachedDriftSummary;
    private static object? _cachedAuditSummary;
    private static long _driftCacheTicks;
    private static long _auditCacheTicks;
    private const int HealthSummaryCacheMs = 12_000;

    private static object BuildDriftSummary()
    {
        long now = Environment.TickCount64;
        if (_cachedDriftSummary != null && now - _driftCacheTicks < HealthSummaryCacheMs)
            return _cachedDriftSummary;

        try
        {
            if (_engine == null)
                return _cachedDriftSummary ?? new { driftedCount = 0, total = 0 };
            var entries = _engine.Drift.Scan();
            int drifted = entries.Count(e => e.Status is DriftStatus.Drifted or DriftStatus.Missing);
            _cachedDriftSummary = new { driftedCount = drifted, total = entries.Count };
            _driftCacheTicks = now;
            return _cachedDriftSummary;
        }
        catch
        {
            return _cachedDriftSummary ?? new { driftedCount = 0, total = 0 };
        }
    }

    private static object BuildAuditSummary()
    {
        long now = Environment.TickCount64;
        if (_cachedAuditSummary != null && now - _auditCacheTicks < HealthSummaryCacheMs)
            return _cachedAuditSummary;

        try
        {
            if (_engine == null)
                return _cachedAuditSummary ?? new { issueCount = 0 };
            // Exclude always-present active-state heartbeat so badge reflects real issues
            int issues = _engine.Audit.Scan()
                .Count(f => !string.Equals(f.Id, "audit.active_state", StringComparison.OrdinalIgnoreCase));
            _cachedAuditSummary = new { issueCount = issues };
            _auditCacheTicks = now;
            return _cachedAuditSummary;
        }
        catch
        {
            return _cachedAuditSummary ?? new { issueCount = 0 };
        }
    }

    /// <summary>Call after apply/revert/fix so the next getState rescans health.</summary>
    private static void InvalidateHealthSummaryCache()
    {
        _driftCacheTicks = 0;
        _auditCacheTicks = 0;
    }

    private static string MapKindToUiId(AntiLagNext.Core.Enums.ProfileKind kind) =>
        OptimizationProfile.UiId(kind);

    /// <summary>Localized profile label for logs / BuildUiState (UI still re-localizes via i18n keys).</summary>
    private static string LocalizeProfileLabel(AntiLagNext.Core.Enums.ProfileKind kind) =>
        OptimizationProfile.LocalizedName(kind, _engine?.Settings.UiCulture);

    /// <summary>
    /// Map engine messages to the active UI culture for the log panel.
    /// Engine source of truth is English; also remaps legacy Russian fragments.
    /// </summary>
    private static string LocalizeEngineMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message ?? "";
        string m = message.Trim();
        bool en = IsEnglishUi();

        // Exact known strings (EN source)
        string? mapped = m switch
        {
            "Profile is not set." => L("Профиль не задан.", "Profile is not set."),
            "Could not prepare safety backup." => L("Не удалось подготовить защиту.", "Could not prepare safety backup."),
            "Profile apply failed." => L("Сбой применения профиля.", "Profile apply failed."),
            "Created default settings." => L("Созданы настройки по умолчанию.", "Created default settings."),
            "Settings loaded." => L("Настройки загружены.", "Settings loaded."),
            "Settings loaded and migrated." => L("Настройки загружены и мигрированы.", "Settings loaded and migrated."),
            "Settings saved." => L("Настройки сохранены.", "Settings saved."),
            "Could not save settings." => L("Не удалось сохранить настройки.", "Could not save settings."),
            "Timer released; system will return to default resolution." =>
                L("Таймер отпущен, система вернётся к разрешению по умолчанию.", m),
            "Could not release timer." => L("Не удалось отпустить таймер.", m),
            "Timer calibration cancelled." => L("Подбор таймера отменён.", m),
            "Timer resolution calibration failed." => L("Сбой подбора разрешения таймера.", m),
            "Game Mode on." => L("Game Mode включён.", m),
            "Game Mode on, Game DVR off." => L("Game Mode включён, Game DVR отключён.", m),
            "Game Mode off." => L("Game Mode выключен.", m),
            "HAGS on (reboot may be required)." => L("HAGS включён (может потребоваться перезагрузка).", m),
            "HAGS off (reboot may be required)." => L("HAGS выключен (может потребоваться перезагрузка).", m),
            "Registry write denied (administrator required)." =>
                L("Нет прав на запись реестра (нужен администратор).", m),
            "Could not change Game Mode." => L("Не удалось изменить Game Mode.", m),
            "HAGS write denied (administrator / HKLM required)." =>
                L("Нет прав на HAGS (нужен администратор / HKLM).", m),
            "Could not change HAGS." => L("Не удалось изменить HAGS.", m),
            "Core parking: system default (unchanged)." =>
                L("Парковка: системное значение по умолчанию (без изменений).", m),
            "Could not apply core activity mode." => L("Не удалось применить режим активности ядер.", m),
            "Power setting applied." => L("Настройка питания применена.", m),
            "Could not activate power scheme." => L("Не удалось активировать схему.", m),
            "Could not write power setting." => L("Не удалось записать значение настройки.", m),
            "Could not start powercfg." => L("Не удалось запустить powercfg.", m),
            "Power setting made visible." => L("Настройка сделана видимой.", m),
            "Could not unhide power setting." => L("Не удалось снять скрытие настройки.", m),
            "Extra plugins: nothing to apply (all opt-in off or applied-by-core)." =>
                L("Доп. плагины: нечего применять (все opt-in выкл. или applied-by-core).", m),
            "Plugins: errors" => L("Плагины: ошибки", m),
            "Pre-rendered frames: unchanged." => L("Pre-rendered frames: без изменений.", m),
            "Could not apply GPU Low Latency." => L("Не удалось применить GPU Low Latency.", m),
            "GPU vendor unknown. Install NVIDIA/AMD drivers." =>
                L("GPU-вендор не определён. Установите драйвер NVIDIA/AMD.", m),
            "Safety backup prepared." => L("Защита перед изменениями подготовлена.", m),
            "All optimizations reset (timer, backup, plugins)." =>
                L("Все оптимизации сброшены (таймер, бэкап, плагины).", m),
            "Reset completed partially." => L("Сброс выполнен частично.", m),
            "JSON backup not found — soft fallback." => L("Бэкап JSON не найден — soft fallback.", m),
            "Scheme: Balanced" => L("Схема: Balanced", m),
            "Active power scheme restored" => L("Активная схема восстановлена", m),
            "System Restore is disabled." => L("System Restore отключён.", m),
            "Ultimate Performance unavailable — using High Performance." =>
                L("Ultimate Performance недоступна — использована High Performance.", m),
            "Could not find a stable timer resolution." =>
                L("Не удалось подобрать стабильное разрешение таймера.", m),
            "Memory cleanup failed." => L("Ошибка очистки памяти.", m),
            "Could not get active power scheme." => L("Не удалось получить активную схему.", m),
            "Backup session not found." => L("Сессия бэкапа не найдена.", m),
            "Could not save backup." => L("Не удалось сохранить бэкап.", m),
            "No backups yet." => L("Бэкапов пока нет.", m),
            "Backup file not found." => L("Файл бэкапа не найден.", m),
            "Backup deleted." => L("Бэкап удалён.", m),
            "Empty backup record." => L("Пустая запись бэкапа.", m),
            "Optimization reset failed." => L("Сбой при сбросе оптимизаций.", m),
            _ => null
        };
        if (mapped != null) return mapped;

        // Profile 'X' applied. / applied partially. (+ optional trailing detail)
        if (m.StartsWith("Profile '", StringComparison.Ordinal) && m.Contains("' applied partially.", StringComparison.Ordinal))
        {
            string name = ExtractQuoted(m) ?? m;
            string tail = TailAfter(m, "' applied partially.");
            return L($"Профиль «{name}» применён частично.", $"Profile '{name}' applied partially.")
                   + LocalizeEngineFragments(tail);
        }
        if (m.StartsWith("Profile '", StringComparison.Ordinal) && m.Contains("' applied.", StringComparison.Ordinal))
        {
            string name = ExtractQuoted(m) ?? m;
            string tail = TailAfter(m, "' applied.");
            return L($"Профиль «{name}» применён.", $"Profile '{name}' applied.")
                   + LocalizeEngineFragments(tail);
        }

        // Composite detail: localize known fragments (EN↔RU)
        string frag = LocalizeEngineFragments(m);

        // If UI is English but message still contains Cyrillic (legacy), best-effort map
        if (en && ContainsCyrillic(frag))
            frag = LocalizeRussianLegacyToEnglish(frag);

        return frag;
    }

    private static bool ContainsCyrillic(string s)
    {
        foreach (char c in s)
        {
            if (c is >= 'А' and <= 'я' or 'Ё' or 'ё')
                return true;
        }
        return false;
    }

    /// <summary>Translate common English engine fragments to RU when UI is Russian (and reverse fragments).</summary>
    private static string LocalizeEngineFragments(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        bool en = IsEnglishUi();
        // Order: longer phrases first
        (string enText, string ruText)[] pairs =
        {
            ("Timer released; system will return to default resolution.", "Таймер отпущен, система вернётся к разрешению по умолчанию."),
            ("HAGS on (reboot may be required).", "HAGS включён (может потребоваться перезагрузка)."),
            ("HAGS off (reboot may be required).", "HAGS выключен (может потребоваться перезагрузка)."),
            ("Game Mode on, Game DVR off.", "Game Mode включён, Game DVR отключён."),
            ("Game Mode on.", "Game Mode включён."),
            ("Game Mode off.", "Game Mode выключен."),
            ("Core parking: system default (unchanged).", "Парковка: системное значение по умолчанию (без изменений)."),
            ("Core parking: all cores active", "Парковка: все ядра активны"),
            ("Core parking: P-cores active, E-cores may park", "Парковка: P-cores активны, E-cores могут парковаться"),
            ("all cores active (homogeneous CPU)", "все ядра активны (однородный CPU)"),
            ("Power setting applied.", "Настройка питания применена."),
            ("Power scheme ", "Схема питания "),
            (" activated.", " активирована."),
            ("Extra plugins: nothing to apply (all opt-in off or applied-by-core).",
                "Доп. плагины: нечего применять (все opt-in выкл. или applied-by-core)."),
            ("NVIDIA: Low Latency Mode (registry) on. Use in-game Reflex for best results.",
                "NVIDIA: Low Latency Mode (registry) включён. Для Reflex — в настройках игры."),
            ("NVIDIA: Low Latency Mode (registry) off.", "NVIDIA: Low Latency Mode (registry) выключен."),
            ("AMD: Anti-Lag (registry) on. Check Radeon Software.",
                "AMD: Anti-Lag (registry) включён. Проверьте Radeon Software."),
            ("AMD: Anti-Lag (registry) off.", "AMD: Anti-Lag (registry) выключен."),
            ("Intel GPU: Low Latency is not automated (set in Graphics Command Center).",
                "Intel GPU: Low Latency через драйвер Xe не автоматизируется (ручная настройка в Graphics Command Center)."),
            ("Pre-rendered frames: unchanged.", "Pre-rendered frames: без изменений."),
            ("All optimizations reset (timer, backup, plugins).",
                "Все оптимизации сброшены (таймер, бэкап, плагины)."),
            ("Reset completed partially.", "Сброс выполнен частично."),
            ("JSON backup not found — soft fallback.", "Бэкап JSON не найден — soft fallback."),
            ("Active power scheme restored", "Активная схема восстановлена"),
            ("Scheme: Balanced", "Схема: Balanced"),
            ("Safety backup prepared.", "Защита перед изменениями подготовлена."),
            ("Ultimate Performance unavailable — using High Performance.",
                "Ultimate Performance недоступна — использована High Performance."),
            ("Could not find a stable timer resolution.", "Не удалось подобрать стабильное разрешение таймера."),
            ("Memory: trimmed ", "Очищено процессов: "),
            (" processes, ≈ ", ", освобождено ≈ "),
            (" MB freed, skipped ", " МБ, пропущено: "),
            ("Changes committed.", "Изменения зафиксированы."),
            ("Backup saved:", "Бэкап сохранён:"),
            ("Timer: ", "Таймер: "),
            (" ms (jitter ", " мс (джиттер "),
            (" µs).", " мкс)."),
            (" (jitter ", " (джиттер "),
        };

        string result = text;
        foreach (var (enText, ruText) in pairs)
        {
            if (en)
            {
                if (result.Contains(ruText, StringComparison.Ordinal))
                    result = result.Replace(ruText, enText, StringComparison.Ordinal);
            }
            else
            {
                if (result.Contains(enText, StringComparison.Ordinal))
                    result = result.Replace(enText, ruText, StringComparison.Ordinal);
            }
        }
        return result;
    }

    /// <summary>Best-effort map of common legacy Russian engine strings when UI is English.</summary>
    private static string LocalizeRussianLegacyToEnglish(string m)
    {
        string r = m;
        (string ru, string en)[] pairs =
        {
            ("Таймер отпущен, система вернётся к разрешению по умолчанию.",
                "Timer released; system will return to default resolution."),
            ("Game Mode включён, Game DVR отключён.", "Game Mode on, Game DVR off."),
            ("Game Mode включён", "Game Mode on"),
            ("Game Mode выключен.", "Game Mode off."),
            ("HAGS включён (может потребоваться перезагрузка).", "HAGS on (reboot may be required)."),
            ("HAGS выключен (может потребоваться перезагрузка).", "HAGS off (reboot may be required)."),
            ("Парковка: системное значение по умолчанию (без изменений).",
                "Core parking: system default (unchanged)."),
            ("Парковка: Все ядра активны", "Core parking: all cores active"),
            ("Парковка: все ядра активны", "Core parking: all cores active"),
            ("P-cores активны, E-cores могут парковаться", "P-cores active, E-cores may park"),
            ("Настройка питания применена.", "Power setting applied."),
            ("активирована.", "activated."),
            ("Схема ", "Power scheme "),
            ("Доп. плагины: нечего применять (все opt-in выкл. или applied-by-core).",
                "Extra plugins: nothing to apply (all opt-in off or applied-by-core)."),
            ("Плагины: ошибки", "Plugins: errors"),
            ("Таймер: ", "Timer: "),
            (" мс (джиттер ", " ms (jitter "),
            (" мкс).", " µs)."),
            ("применён частично", "applied partially"),
            ("применён.", "applied."),
            ("Профиль «", "Profile '"),
            ("» ", "' "),
            ("Не удалось ", "Could not "),
            ("Нет прав на запись реестра (нужен администратор).",
                "Registry write denied (administrator required)."),
        };
        foreach (var (ru, en) in pairs)
        {
            if (r.Contains(ru, StringComparison.Ordinal))
                r = r.Replace(ru, en, StringComparison.Ordinal);
        }
        return r;
    }

    private static string? ExtractQuoted(string m)
    {
        int a = m.IndexOf('\'');
        int b = m.IndexOf('\'', a + 1);
        if (a < 0 || b <= a) return null;
        return m.Substring(a + 1, b - a - 1);
    }

    private static string TailAfter(string m, string marker)
    {
        int i = m.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return "";
        return m[(i + marker.Length)..];
    }

    private static string LocalizeProfileLabelFromToken(string? token, ProfileKind fallbackKind)
    {
        if (string.IsNullOrWhiteSpace(token))
            return LocalizeProfileLabel(fallbackKind);

        string k = token.Trim();
        // Stable UI ids stored in ActiveState after apply
        if (k.Equals("gaming", StringComparison.OrdinalIgnoreCase)
            || k.Equals("game", StringComparison.OrdinalIgnoreCase)
            || k.Equals("игровой", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Gaming", StringComparison.Ordinal))
            return LocalizeProfileLabel(ProfileKind.Gaming);
        if (k.Equals("office", StringComparison.OrdinalIgnoreCase)
            || k.Equals("офисный", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Office", StringComparison.Ordinal))
            return LocalizeProfileLabel(ProfileKind.Office);
        if (k.Equals("max", StringComparison.OrdinalIgnoreCase)
            || k.Equals("maxperformance", StringComparison.OrdinalIgnoreCase)
            || k.Equals("maximum", StringComparison.OrdinalIgnoreCase)
            || k.Contains("Максимал", StringComparison.OrdinalIgnoreCase)
            || k.Contains("Maximum", StringComparison.OrdinalIgnoreCase))
            return LocalizeProfileLabel(ProfileKind.MaxPerformance);
        if (k.Equals("default", StringComparison.OrdinalIgnoreCase)
            || k.Equals("off", StringComparison.OrdinalIgnoreCase)
            || k.Equals("По умолчанию", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Default", StringComparison.Ordinal))
            return LocalizeProfileLabel(ProfileKind.Default);

        return LocalizeProfileLabel(fallbackKind);
    }

    /// <summary>Windows user preference: AppsUseLightTheme (1 = light).</summary>
    private static bool OsPrefersLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void Reply(string? id, object payload)
    {
        if (_window == null) return;
        var envelope = new { id, type = "reply", payload };
        string json = JsonSerializer.Serialize(envelope, JsonOpts);
        SafeSend(json);
    }

    private static void SafeSend(string json)
    {
        // Serialize all native sends — Photino is not concurrent-safe.
        lock (SendLock)
        {
            try
            {
                _window?.SendWebMessage(json);
            }
            catch (Exception ex)
            {
                WriteCrash("SendWebMessage", ex);
            }
        }
    }

    private static void AddLog(string message, string level)
    {
        lock (LogLock)
        {
            LogBuffer.Insert(0, new
            {
                time = DateTime.Now.ToString("HH:mm:ss"),
                message,
                level
            });
            while (LogBuffer.Count > 100)
                LogBuffer.RemoveAt(LogBuffer.Count - 1);
        }
    }

    private static List<object> SnapshotLogs()
    {
        lock (LogLock)
            return LogBuffer.ToList();
    }

    private static void WriteCrash(string where, Exception? ex)
    {
        try
        {
            string dir = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:O}] {where}: {ex}\n\n");
            Trace.TraceError("{0}: {1}", where, ex);
        }
        catch { /* ignore */ }
    }

    private static void MessageBoxNative(string text)
    {
        try { MessageBoxW(IntPtr.Zero, text, "AntiLag Next", 0x10); }
        catch { Console.Error.WriteLine(text); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
