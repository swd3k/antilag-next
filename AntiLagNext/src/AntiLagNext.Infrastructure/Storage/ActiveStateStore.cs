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
            // Normalize legacy Russian display names to stable UI keys
            string? key = NormalizeProfileToken(profileName);
            var state = new ActiveOptimizationState
            {
                Active = true,
                AppliedUtc = DateTime.UtcNow,
                ProfileName = key
            };
            File.WriteAllText(AppPaths.ActiveStateFile, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Map legacy display names ("Игровой", "Gaming") and ids to stable keys: gaming|office|max|default.
    /// </summary>
    public static string? NormalizeProfileToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return token;
        string k = token.Trim();
        if (k.Equals("gaming", StringComparison.OrdinalIgnoreCase)
            || k.Equals("game", StringComparison.OrdinalIgnoreCase)
            || k.Contains("игровой", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Gaming", StringComparison.Ordinal))
            return "gaming";
        if (k.Equals("office", StringComparison.OrdinalIgnoreCase)
            || k.Contains("офис", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Office", StringComparison.Ordinal))
            return "office";
        if (k.Equals("max", StringComparison.OrdinalIgnoreCase)
            || k.Equals("maxperformance", StringComparison.OrdinalIgnoreCase)
            || k.Equals("maximum", StringComparison.OrdinalIgnoreCase)
            || k.Contains("максимал", StringComparison.OrdinalIgnoreCase)
            || k.Contains("Maximum", StringComparison.OrdinalIgnoreCase))
            return "max";
        if (k.Equals("default", StringComparison.OrdinalIgnoreCase)
            || k.Equals("off", StringComparison.OrdinalIgnoreCase)
            || k.Contains("умолчан", StringComparison.OrdinalIgnoreCase)
            || k.Equals("Default", StringComparison.Ordinal))
            return "default";
        return k;
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
