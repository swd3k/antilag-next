using System.Text.Json;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Storage;

/// <summary>
/// Флаг «оптимизации применены нами» — не путать с «у пользователя уже High Performance».
/// </summary>
public sealed class ActiveOptimizationState
{
    public bool Active { get; set; }
    public DateTime AppliedUtc { get; set; }
    public string? ProfileName { get; set; }
    public Guid? BackupSessionHint { get; set; }
}

public static class ActiveStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static ActiveOptimizationState Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ActiveStateFile))
                return new ActiveOptimizationState();
            var json = File.ReadAllText(AppPaths.ActiveStateFile);
            return JsonSerializer.Deserialize<ActiveOptimizationState>(json, JsonOpts)
                   ?? new ActiveOptimizationState();
        }
        catch
        {
            return new ActiveOptimizationState();
        }
    }

    public static void MarkActive(string? profileName)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var state = new ActiveOptimizationState
            {
                Active = true,
                AppliedUtc = DateTime.UtcNow,
                ProfileName = profileName
            };
            File.WriteAllText(AppPaths.ActiveStateFile, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    public static void MarkInactive()
    {
        try
        {
            AppPaths.EnsureDirectories();
            var state = new ActiveOptimizationState { Active = false, AppliedUtc = DateTime.UtcNow };
            File.WriteAllText(AppPaths.ActiveStateFile, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    public static bool IsActive() => Load().Active;
}
