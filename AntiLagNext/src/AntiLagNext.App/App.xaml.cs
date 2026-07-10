using System.Windows;
using AntiLagNext.App.ViewModels;
using AntiLagNext.App.Views;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Infrastructure;
using AntiLagNext.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AntiLagNext.App;

/// <summary>
/// Точка входа WPF: Serilog + Generic Host + DI + MainWindow.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        // XAML/binding-ошибки (напр. Tips) иначе валят процесс без MessageBox.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                path: System.IO.Path.Combine(AppPaths.LogsDirectory, "antilag-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((ctx, services) =>
                {
                    services.AddAntiLagNextInfrastructure();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<DashboardViewModel>();
                    services.AddSingleton<ProfilesViewModel>();
                    services.AddSingleton<MonitoringViewModel>();
                    services.AddSingleton<BackupsViewModel>();
                    services.AddSingleton<PluginsViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<TipsViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            // Plugin discovery (built-in + plugins/*.dll)
            var plugins = _host.Services.GetRequiredService<IPluginCatalog>();
            await plugins.LoadAsync();

            // Apply theme + verify i18n before first paint
            try
            {
                var settings = _host.Services.GetRequiredService<AntiLagNext.Core.Abstractions.ISettingsService>();
                AntiLagNext.App.Services.AppThemeService.Apply(settings.Current.Theme);
                var loc = _host.Services.GetRequiredService<AntiLagNext.Core.Localization.ILocalizationService>();
                string sample = loc.T("page.dashboard.title");
                Log.Information("i18n sample page.dashboard.title={0} culture={1}", sample, loc.CurrentCulture);
                if (sample.StartsWith("page.", StringComparison.OrdinalIgnoreCase))
                    Log.Warning("i18n keys not resolved — check i18n folder / embedded resources");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Theme/i18n pre-paint failed");
            }

            var main = _host.Services.GetRequiredService<MainWindow>();
            main.Show();
            Log.Information("AntiLag Next запущен. Plugins: {Count}", plugins.Plugins.Count);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Критический сбой при старте");
            MessageBox.Show(
                $"Не удалось запустить AntiLag Next:\n{ex.Message}",
                "AntiLag Next",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Необработанное исключение UI");
        MessageBox.Show(
            $"Ошибка интерфейса:\n{e.Exception.GetBaseException().Message}",
            "AntiLag Next",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "Необработанное исключение AppDomain (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("Необработанное исключение AppDomain: {Object}", e.ExceptionObject);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ошибка при остановке host");
        }
        finally
        {
            Log.CloseAndFlush();
        }
        base.OnExit(e);
    }
}
