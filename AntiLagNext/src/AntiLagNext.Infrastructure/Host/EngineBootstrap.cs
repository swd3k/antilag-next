using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Safety;
using AntiLagNext.Infrastructure.Services;
using AntiLagNext.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AntiLagNext.Infrastructure.Host;

/// <summary>
/// Minimal DI host for CLI and Photino UI (no Microsoft.Extensions.Hosting bloat).
/// </summary>
public sealed class EngineBootstrap : IDisposable
{
    private readonly ServiceProvider _provider;

    public IServiceProvider Services => _provider;
    public AppSettings Settings { get; }
    public IProfileService Profiles { get; }
    public ISafetyService Safety { get; }
    public IPluginCatalog Plugins { get; }
    public ITimerManager Timer { get; }
    public IMonitoringService Monitoring { get; }
    public ISettingsService SettingsService { get; }
    public IDriftService Drift { get; }
    public IAuditService Audit { get; }
    public IUpdateService Update { get; }
    public DiagnosticsExportService Diagnostics { get; }

    /// <summary>
    /// Last profile-apply "What changed" summary (null after Revert or before first Apply).
    /// </summary>
    public ApplyChangeSummary? LastApplySummary =>
        Profiles is Optimization.ProfileService ps ? ps.LastApplySummary : null;

    private EngineBootstrap(ServiceProvider provider)
    {
        _provider = provider;
        Settings = provider.GetRequiredService<AppSettings>();
        Profiles = provider.GetRequiredService<IProfileService>();
        Safety = provider.GetRequiredService<ISafetyService>();
        Plugins = provider.GetRequiredService<IPluginCatalog>();
        Timer = provider.GetRequiredService<ITimerManager>();
        Monitoring = provider.GetRequiredService<IMonitoringService>();
        SettingsService = provider.GetRequiredService<ISettingsService>();
        Drift = provider.GetRequiredService<IDriftService>();
        Audit = provider.GetRequiredService<IAuditService>();
        Update = provider.GetRequiredService<IUpdateService>();
        Diagnostics = provider.GetRequiredService<DiagnosticsExportService>();
    }

    public static async Task<EngineBootstrap> CreateAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        var sc = new ServiceCollection();
        sc.AddAntiLagNextInfrastructure();
        var provider = sc.BuildServiceProvider();
        var engine = new EngineBootstrap(provider);

        var recover = await ApplySessionGuard.RecoverIfNeededAsync(engine.Safety, cancellationToken)
            .ConfigureAwait(false);
        if (recover != null)
            System.Diagnostics.Trace.TraceWarning("Recovery: {0}", recover.Message);

        await engine.Plugins.LoadAsync(cancellationToken).ConfigureAwait(false);
        return engine;
    }

    public OptimizationProfile ResolveProfile(string? nameOrKind)
    {
        if (string.IsNullOrWhiteSpace(nameOrKind))
            return Settings.GetActiveProfile();

        string key = nameOrKind.Trim();
        // CLI aliases
        if (key.Equals("gaming", StringComparison.OrdinalIgnoreCase)
            || key.Equals("game", StringComparison.OrdinalIgnoreCase)
            || key.Equals("игровой", StringComparison.OrdinalIgnoreCase))
            return Settings.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Gaming)
                   ?? OptimizationProfile.CreatePreset(ProfileKind.Gaming);

        if (key.Equals("office", StringComparison.OrdinalIgnoreCase)
            || key.Equals("офисный", StringComparison.OrdinalIgnoreCase))
            return Settings.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Office)
                   ?? OptimizationProfile.CreatePreset(ProfileKind.Office);

        if (key.Equals("max", StringComparison.OrdinalIgnoreCase)
            || key.Equals("maxperformance", StringComparison.OrdinalIgnoreCase)
            || key.Equals("maximum", StringComparison.OrdinalIgnoreCase))
            return Settings.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.MaxPerformance)
                   ?? OptimizationProfile.CreatePreset(ProfileKind.MaxPerformance);

        if (key.Equals("default", StringComparison.OrdinalIgnoreCase)
            || key.Equals("off", StringComparison.OrdinalIgnoreCase))
            return Settings.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Default)
                   ?? OptimizationProfile.CreatePreset(ProfileKind.Default);

        return Settings.Profiles.FirstOrDefault(p =>
                   p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? Settings.GetActiveProfile();
    }

    public async Task<OperationResult> ApplyAsync(string? profileName, CancellationToken ct = default)
    {
        var profile = ResolveProfile(profileName);
        Settings.ActiveProfileId = profile.Id;
        if (!Settings.Profiles.Any(p => p.Id == profile.Id))
            Settings.Profiles.Add(profile);
        SettingsService.Save();
        return await Profiles.ApplyAsync(profile, ct).ConfigureAwait(false);
    }

    public async Task<OperationResult> RevertAsync(CancellationToken ct = default)
    {
        // Route through ProfileService so LastApplySummary is cleared.
        var result = await Profiles.RevertAsync(ct).ConfigureAwait(false);
        return result;
    }

    public object BuildStatusSnapshot()
    {
        var active = ActiveStateStore.Load();
        var timer = Timer.CurrentState;
        return new
        {
            optimized = active.Active,
            // Prefer stable key (gaming/office/max); fall back to stored name / preset name
            profile = active.ProfileName
                      ?? OptimizationProfile.UiId(Settings.GetActiveProfile().Kind),
            profileName = Settings.GetActiveProfile().Name,
            timerMs = timer.ActualMs,
            timerHeld = timer.IsActive,
            incompleteApply = ApplySessionGuard.HasIncompleteApply(),
            plugins = Plugins.Plugins.Select(p =>
            {
                bool supported = p.IsSupported(out var reason);
                var st = p.GetStatus();
                return new
                {
                    id = p.Id,
                    enabled = p.IsEnabled,
                    appliedByCore = p.AppliedByCore,
                    category = p.Category.ToString(),
                    supported,
                    reason,
                    state = st.State.ToString(),
                    message = st.Message
                };
            }).ToList()
        };
    }

    public void Dispose() => _provider.Dispose();
}
