using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Localization;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Localization;
using AntiLagNext.Infrastructure.Optimization;
using AntiLagNext.Infrastructure.Plugins;
using AntiLagNext.Infrastructure.Safety;
using AntiLagNext.Infrastructure.Services;
using AntiLagNext.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AntiLagNext.Infrastructure;

/// <summary>
/// Регистрация Infrastructure: core managers + plugin catalog + i18n.
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

        // i18n
        services.AddSingleton<ILocalizationService>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            var loc = new JsonLocalizationService(AppPaths.I18nDirectory);
            if (!string.IsNullOrWhiteSpace(settings.UiCulture))
                loc.SetCulture(settings.UiCulture);
            return loc;
        });

        // Core optimization engine
        services.AddSingleton<ITimerManager, TimerManager>();
        services.AddSingleton<IPowerManager, PowerManager>();
        services.AddSingleton<ICoreParkingManager, CoreParkingManager>();
        services.AddSingleton<IGameModeManager, GameModeManager>();
        services.AddSingleton<IMemoryManager, MemoryManager>();
        services.AddSingleton<IGpuManager, GpuManager>();

        // Safety + mutual exclusion for system mutations (apply / reset / registry)
        services.AddSingleton<SystemMutationGate>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ISafetyService, SafetyService>();

        // Plugins (before ProfileService — injects IPluginCatalog)
        services.AddSingleton<IPluginCatalog, PluginCatalog>();

        // Orchestration
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IMonitoringService, MonitoringService>();
        services.AddSingleton<IBenchmarkService, BenchmarkService>();

        return services;
    }
}
