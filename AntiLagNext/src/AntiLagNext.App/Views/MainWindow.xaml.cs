using System.Drawing;
using System.Windows;
using AntiLagNext.App.Services;
using AntiLagNext.App.ViewModels;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Localization;

namespace AntiLagNext.App.Views;

public partial class MainWindow : Window
{
    private readonly MonitoringViewModel _monitoring;
    private readonly ISettingsService _settings;
    private readonly ITimerManager _timer;
    private readonly ILocalizationService _loc;
    private System.Windows.Forms.NotifyIcon? _tray;
    private Icon? _trayIconOwned;
    private bool _forceClose;

    public MainWindow(
        MainViewModel vm,
        ISettingsService settings,
        MonitoringViewModel monitoring,
        ITimerManager timer,
        ILocalizationService loc)
    {
        InitializeComponent();
        _settings = settings;
        _monitoring = monitoring;
        _timer = timer;
        _loc = loc;
        DataContext = vm;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _loc.CultureChanged += (_, _) => RefreshTrayMenuTexts();
        if (vm is MainViewModel mvm)
            mvm.NavigateCommand.Execute("Dashboard");
        InitTray();
    }

    private void InitTray()
    {
        try
        {
            // Cropped PNG → ≥32px HiDPI tray glyph (256-only ICO looked tiny/blurry)
            _trayIconOwned = IconHelper.LoadTrayIcon();

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = _trayIconOwned,
                Text = "AntiLag Next",
                Visible = true
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();

            _tray.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            RefreshTrayMenuTexts();
        }
        catch
        {
            // tray is best-effort
        }
    }

    private void RefreshTrayMenuTexts()
    {
        if (_tray?.ContextMenuStrip is not { } menu) return;
        menu.Items.Clear();
        menu.Items.Add(_loc.T("tray.open"), null, (_, _) => ShowFromTray());
        menu.Items.Add(_loc.T("tray.reset"), null, async (_, _) =>
        {
            var confirm = MessageBox.Show(
                _loc.T("confirm.reset.body"),
                _loc.T("confirm.reset.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            if (DataContext is MainViewModel m)
                await m.Dashboard.ResetAllConfirmedAsync();
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_loc.T("tray.exit"), null, (_, _) => ExitApp());
        _tray.Text = _loc.T("tray.tooltip");
        _tray.BalloonTipTitle = _loc.T("tray.balloon.title");
        _tray.BalloonTipText = _loc.T("tray.balloon.text");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.Current.MonitoringEnabled)
            _monitoring.StartCommand.Execute(null);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _monitoring.StopCommand.Execute(null);
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayIconOwned?.Dispose();
        _trayIconOwned = null;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray)
        {
            Hide();
            if (_tray != null)
            {
                _tray.BalloonTipTitle = _loc.T("tray.balloon.title");
                _tray.BalloonTipText = _loc.T("tray.balloon.text");
                _tray.ShowBalloonTip(2000);
            }
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;

        if (_settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            Hide();
            return;
        }

        if (_settings.Current.ReleaseTimerOnExit)
            _timer.Release();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        var result = MessageBox.Show(
            _loc.T(_settings.Current.ReleaseTimerOnExit ? "tray.exit.confirm.release" : "tray.exit.confirm.keep"),
            _loc.T("app.name"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        if (_settings.Current.ReleaseTimerOnExit)
            _timer.Release();

        _forceClose = true;
        Close();
        Application.Current.Shutdown();
    }
}
