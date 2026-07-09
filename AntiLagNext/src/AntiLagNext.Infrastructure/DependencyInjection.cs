using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Optimization;
using AntiLagNext.Infrastructure.Safety;
using AntiLagNext.Infrastructure.Services;
using AntiLagNext.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AntiLagNext.Infrastructure;

/// <summary>
/// Регистрация всех сервисов Infrastructure в DI-контейнере.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAntiLagNextInfrastructure(this IServiceCollection services)
    {
        AppPaths.EnsureDirectories();

        // Settings — singleton, загружается сразу
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            settings.Load();
            return settings.Current;
        });

        // Core optimization engine
        services.AddSingleton<ITimerManager, TimerManager>();
        services.AddSingleton<IPowerManager, PowerManager>();
        services.AddSingleton<ICoreParkingManager, CoreParkingManager>();
        services.AddSingleton<IGameModeManager, GameModeManager>();
        services.AddSingleton<IMemoryManager, MemoryManager>();
        services.AddSingleton<IGpuManager, GpuManager>();

        // Safety
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ISafetyService, SafetyService>();

        // Orchestration
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IMonitoringService, MonitoringService>();
        services.AddSingleton<IBenchmarkService, BenchmarkService>();

        return services;
    }
}
