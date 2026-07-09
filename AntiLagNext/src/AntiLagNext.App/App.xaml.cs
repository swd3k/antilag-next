using System.Windows;
using AntiLagNext.App.ViewModels;
using AntiLagNext.App.Views;
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
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<TipsViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            var main = _host.Services.GetRequiredService<MainWindow>();
            main.Show();
            Log.Information("AntiLag Next запущен.");
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
