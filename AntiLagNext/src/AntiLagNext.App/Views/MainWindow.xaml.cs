using System.Drawing;
using System.Windows;
using AntiLagNext.App.ViewModels;
using AntiLagNext.Core.Abstractions;

namespace AntiLagNext.App.Views;

public partial class MainWindow : Window
{
    private readonly MonitoringViewModel _monitoring;
    private readonly ISettingsService _settings;
    private readonly ITimerManager _timer;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _forceClose;

    public MainWindow(
        MainViewModel vm,
        ISettingsService settings,
        MonitoringViewModel monitoring,
        ITimerManager timer)
    {
        InitializeComponent();
        _settings = settings;
        _monitoring = monitoring;
        _timer = timer;
        DataContext = vm;
        Loaded += OnLoaded;
        Closed += OnClosed;
        InitTray();
    }

    private void InitTray()
    {
        try
        {
            Icon? icon = null;
            var icoUri = new Uri("pack://application:,,,/Assets/app.ico");
            var streamInfo = Application.GetResourceStream(icoUri);
            if (streamInfo != null)
            {
                using var s = streamInfo.Stream;
                icon = new Icon(s);
            }

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon ?? SystemIcons.Application,
                Text = "AntiLag Next",
                Visible = true
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
            menu.Items.Add("Сбросить оптимизации", null, async (_, _) =>
            {
                if (DataContext is MainViewModel m)
                    await m.Dashboard.ResetAllCommand.ExecuteAsync(null);
            });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Выход", null, (_, _) => ExitApp());
            _tray.ContextMenuStrip = menu;
        }
        catch
        {
            // tray is best-effort
        }
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
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray)
        {
            Hide();
            if (_tray != null)
            {
                _tray.BalloonTipTitle = "AntiLag Next";
                _tray.BalloonTipText = "Работает в трее. Таймер удерживается в этом процессе.";
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

        // Полный выход: опционально отпустить таймер
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
            _settings.Current.ReleaseTimerOnExit
                ? "Выйти и отпустить разрешение таймера?"
                : "Выйти? Разрешение таймера останется до завершения процесса.",
            "AntiLag Next",
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
