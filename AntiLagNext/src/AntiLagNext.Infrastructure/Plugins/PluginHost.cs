using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Plugins.BuiltIn;
using AntiLagNext.Infrastructure.Storage;

namespace AntiLagNext.Infrastructure.Plugins;

/// <summary>
/// Загрузка built-in + external plugins из {BaseDir}/plugins/*.dll.
/// ApplyEnabledExtensions — только плагины с AppliedByCore=false.
/// External plugins/*.dll require AppSettings.AllowExternalPlugins (default false).
/// </summary>
public sealed class PluginCatalog : IPluginCatalog, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IBackupService? _backup;
    private readonly List<IAntiLagPlugin> _plugins = new();
    private readonly List<PluginLoadContext> _loadContexts = new();
    private readonly PluginServices _services;
    private bool _loaded;

    public IReadOnlyList<IAntiLagPlugin> Plugins => _plugins;

    public PluginCatalog(AppSettings settings, IBackupService backup)
    {
        _settings = settings;
        _backup = backup;
        _services = new PluginServices();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;
        _loaded = true;

        Directory.CreateDirectory(AppPaths.PluginsDirectory);

        // Built-in first (order applied via ApplyOrder)
        RegisterBuiltIn(new TimerCorePlugin());
        RegisterBuiltIn(new PowerCorePlugin());
        RegisterBuiltIn(new GpuCorePlugin());
        RegisterBuiltIn(new NetworkQosPlugin(_backup));
        RegisterBuiltIn(new NetworkHygienePlugin());
        RegisterBuiltIn(new RegistryTweaksPlugin(_backup));
        RegisterBuiltIn(new ProcessPriorityPlugin());
        RegisterBuiltIn(new ServiceOptimizerPlugin(_backup));
        // Experimental — always opt-in, never in Quick Boost defaults
        RegisterBuiltIn(new ExperimentalMsiPlugin());
        RegisterBuiltIn(new ExperimentalInterruptAffinityPlugin());
        RegisterBuiltIn(new ExperimentalDriverBlacklistPlugin());

        // External plugins/*.dll are off-by-default (elevation risk)
        if (_settings.AllowExternalPlugins)
        {
            Trace.TraceWarning(
                "AllowExternalPlugins=true: loading DLLs from {0}. Third-party plugins run with app privileges (often elevated).",
                AppPaths.PluginsDirectory);
            LoadExternalAssemblies();
        }
        else
        {
            Trace.TraceInformation(
                "External plugins skipped (AllowExternalPlugins=false). Built-in only. Set AppSettings.AllowExternalPlugins to enable plugins/*.dll.");
        }

        foreach (var p in _plugins)
        {
            if (_settings.PluginEnabled.TryGetValue(p.Id, out bool en))
                p.IsEnabled = en;
            else if (p.AppliedByCore)
                p.IsEnabled = true;
            else if (p.Category == PluginCategory.Experimental)
                p.IsEnabled = false; // experimental always off by default
            else if (p.Id is "ext.network.qos" or "ext.network.hygiene" or "ext.registry.tweaks")
                p.IsEnabled = true; // safe hygiene defaults for gaming profile extensions
            else if (p.Id == "ext.process.priority")
                p.IsEnabled = true;
            else
                p.IsEnabled = false;

            try
            {
                await p.InitializeAsync(_services, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Plugin init failed {0}: {1}", p.Id, ex.Message);
            }
        }

        Trace.TraceInformation("Plugins loaded: {0} ({1} external)",
            _plugins.Count, _plugins.Count(x => !x.IsBuiltIn));
    }

    private void RegisterBuiltIn(IAntiLagPlugin plugin) => _plugins.Add(plugin);

    private void LoadExternalAssemblies()
    {
        string dir = AppPaths.PluginsDirectory;
        if (!Directory.Exists(dir)) return;

        // Only explicit plugin contract files — never load random *.dll (deps, native, malware drop).
        foreach (var dll in Directory.EnumerateFiles(dir, "*.plugin.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                // Block path tricks
                string full = Path.GetFullPath(dll);
                string pluginsFull = Path.GetFullPath(dir);
                if (!full.StartsWith(pluginsFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetDirectoryName(full), pluginsFull, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.TraceWarning("Plugin path rejected (outside plugins dir): {0}", dll);
                    continue;
                }

                // Size cap against absurd payloads
                var fi = new FileInfo(full);
                if (fi.Length <= 0 || fi.Length > 8 * 1024 * 1024)
                {
                    Trace.TraceWarning("Plugin rejected (size): {0}", dll);
                    continue;
                }

                var alc = new PluginLoadContext(full);
                _loadContexts.Add(alc);
                Assembly asm = alc.LoadFromAssemblyPath(full);
                foreach (var type in asm.GetExportedTypes())
                {
                    if (type.IsAbstract || !typeof(IAntiLagPlugin).IsAssignableFrom(type))
                        continue;
                    if (Activator.CreateInstance(type) is not IAntiLagPlugin plugin)
                        continue;
                    // Basic id hygiene
                    if (string.IsNullOrWhiteSpace(plugin.Id) || plugin.Id.Length > 128
                        || plugin.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        Trace.TraceWarning("Plugin rejected (bad Id) from {0}", Path.GetFileName(full));
                        try { plugin.Dispose(); } catch { /* ignore */ }
                        continue;
                    }
                    _plugins.Add(plugin);
                    Trace.TraceInformation("External plugin: {0} from {1}", plugin.Id, Path.GetFileName(full));
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to load plugin {0}: {1}", dll, ex.Message);
            }
        }
    }

    public IAntiLagPlugin? GetById(string id) =>
        _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task<OperationResult> ApplyEnabledExtensionsAsync(PluginApplyContext context)
    {
        var messages = new List<string>();
        var errors = new List<string>();

        foreach (var p in _plugins
                     .Where(x => x.IsEnabled && !x.AppliedByCore)
                     .OrderBy(x => x.ApplyOrder)
                     .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!p.IsSupported(out var reason))
                {
                    messages.Add($"{p.Id}: skipped ({reason ?? "unsupported"})");
                    continue;
                }

                var r = await p.ApplyAsync(context).ConfigureAwait(false);
                if (r.Success) messages.Add($"{p.Id}: {r.Message}");
                else errors.Add($"{p.Id}: {r.Message}");
            }
            catch (Exception ex)
            {
                errors.Add($"{p.Id}: {ex.Message}");
                Trace.TraceError("Plugin apply failed {0}: {1}", p.Id, ex);
            }
        }

        PersistEnabledFlags();

        if (errors.Count > 0 && messages.Count == 0)
            return OperationResult.Fail("Плагины: ошибки", detail: string.Join("; ", errors));

        string msg = messages.Count == 0
            ? "Доп. плагины: нечего применять (все opt-in выкл. или applied-by-core)."
            : string.Join(" · ", messages);
        if (errors.Count > 0)
            msg += " | warn: " + string.Join("; ", errors);

        return OperationResult.Ok(msg);
    }

    public async Task<OperationResult> RevertAllExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();
        foreach (var p in _plugins.Where(x => !x.AppliedByCore))
        {
            try
            {
                var r = await p.RevertAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(r.Message)) parts.Add(r.Message);
            }
            catch (Exception ex)
            {
                parts.Add($"{p.Id}: {ex.Message}");
            }
        }
        return OperationResult.Ok(parts.Count == 0 ? "Ext plugins: nothing to revert" : string.Join(" · ", parts));
    }

    public void PersistEnabledFlags()
    {
        foreach (var p in _plugins)
            _settings.PluginEnabled[p.Id] = p.IsEnabled;
    }

    public void Dispose()
    {
        foreach (var p in _plugins)
        {
            try { p.Dispose(); } catch { /* ignore */ }
        }
        _plugins.Clear();
        foreach (var c in _loadContexts)
        {
            try { c.Unload(); } catch { /* ignore */ }
        }
        _loadContexts.Clear();
    }
}

internal sealed class PluginServices : IPluginServices
{
    public string PluginsDirectory => AppPaths.PluginsDirectory;
    public string AppDataRoot => AppPaths.AppDataRoot;
    public void LogInfo(string message) => Trace.TraceInformation("[plugin] {0}", message);
    public void LogWarning(string message) => Trace.TraceWarning("[plugin] {0}", message);
    public void LogError(string message, Exception? ex = null)
    {
        if (ex != null) Trace.TraceError("[plugin] {0}: {1}", message, ex);
        else Trace.TraceError("[plugin] {0}", message);
    }
}

/// <summary>Isolated load context for external plugin DLLs.</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Prefer default context for Core shared types
        if (assemblyName.Name is "AntiLagNext.Core" or "System.Runtime" or "netstandard")
            return null;

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }
}
