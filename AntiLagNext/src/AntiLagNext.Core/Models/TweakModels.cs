using System.Text.Json;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Plugins;

namespace AntiLagNext.Core.Models;

/// <summary>Risk tier for registry/system latency tweaks (drives profile defaults).</summary>
public enum TweakRisk
{
    Safe = 0,
    Moderate = 1,
    Aggressive = 2,
    Advanced = 3
}

/// <summary>Desired-state vs live registry comparison result.</summary>
public enum DriftStatus
{
    Ok = 0,
    Drifted = 1,
    Missing = 2
}

/// <summary>
/// Catalog entry for a single registry latency tweak.
/// Core stays free of Microsoft.Win32 — <see cref="ValueKind"/> is the Win32 numeric code
/// (e.g. 4 = REG_DWORD, 1 = REG_SZ).
/// </summary>
public sealed record TweakDefinition
{
    public required string Id { get; init; }
    public required string CategoryId { get; init; }
    public required string NameKey { get; init; }
    public required string DescriptionKey { get; init; }
    public TweakRisk Risk { get; init; } = TweakRisk.Safe;
    public LatencyImpact Impact { get; init; } = LatencyImpact.Low;
    public bool RequiresReboot { get; init; }
    /// <summary>"HKLM" or "HKCU".</summary>
    public required string Hive { get; init; }
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    /// <summary>Microsoft.Win32.RegistryValueKind as int (DWord=4, String=1, …).</summary>
    public int ValueKind { get; init; } = 4;
    /// <summary>JSON-serializable desired value (int, long, string).</summary>
    public object? DesiredValue { get; init; }
    /// <summary>Profiles that auto-apply this tweak (empty = none).</summary>
    public IReadOnlyList<ProfileKind> Profiles { get; init; } = Array.Empty<ProfileKind>();
}

/// <summary>Persisted desired registry state after we apply catalog tweaks.</summary>
public sealed class DesiredStateDocument
{
    public int Version { get; set; } = 1;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<DesiredStateEntry> Entries { get; set; } = new();
}

/// <summary>One expected registry value we own after apply.</summary>
public sealed class DesiredStateEntry
{
    public string TweakId { get; set; } = string.Empty;
    public string Hive { get; set; } = "HKLM";
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>"DWord" or "String".</summary>
    public string Type { get; set; } = "DWord";
    /// <summary>Serialized expected value (decimal for DWord, text for String).</summary>
    public string? Expected { get; set; }
    public string? Category { get; set; }
}

/// <summary>Single drift scan row for UI / reapply.</summary>
public sealed class DriftEntry
{
    public required string TweakId { get; init; }
    public DriftStatus Status { get; init; }
    public string? Current { get; init; }
    public string? Expected { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public string Hive { get; init; } = "HKLM";
}

/// <summary>Audit finding (recommendation, not auto-applied).</summary>
public sealed class AuditFinding
{
    public required string Id { get; init; }
    /// <summary>info | warn | critical</summary>
    public required string Severity { get; init; }
    public required string TitleKey { get; init; }
    public required string Detail { get; init; }
    public string? SuggestedTweakId { get; init; }
    public bool CanFix { get; init; }
    /// <summary>Gpu|Network|Timer|Power|Input|System|Other — for Health UI grouping.</summary>
    public string Area { get; init; } = "Other";
}

/// <summary>Helpers for serializing tweak desired values without Win32 types.</summary>
public static class TweakValueCodec
{
    public const int KindString = 1;
    public const int KindDWord = 4;
    public const int KindQWord = 11;

    public static string TypeName(int valueKind) => valueKind switch
    {
        KindString => "String",
        KindQWord => "QWord",
        _ => "DWord"
    };

    public static string? Serialize(object? value)
    {
        if (value is null) return null;
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.TryGetInt64(out long l) ? l.ToString() : je.GetRawText(),
                JsonValueKind.String => je.GetString(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                _ => je.GetRawText()
            };
        }

        return value switch
        {
            int i => i.ToString(),
            uint u => u.ToString(),
            long l => l.ToString(),
            ulong ul => ul.ToString(),
            string s => s,
            bool b => b ? "1" : "0",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Normalize live registry values for equality checks.</summary>
    public static string? NormalizeLive(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            int i => unchecked((uint)i).ToString(),
            uint u => u.ToString(),
            long l => l.ToString(),
            ulong ul => ul.ToString(),
            string s => s,
            byte b => b.ToString(),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Normalize expected string (supports hex 0x… and signed DWORD -1 → 4294967295).</summary>
    public static string? NormalizeExpected(string? expected, string type)
    {
        if (expected is null) return null;
        if (!type.Equals("DWord", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("QWord", StringComparison.OrdinalIgnoreCase))
            return expected;

        string t = expected.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(t.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out ulong hex))
            return hex.ToString();

        if (long.TryParse(t, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long signed))
        {
            if (type.Equals("DWord", StringComparison.OrdinalIgnoreCase) && signed < 0)
                return unchecked((uint)(int)signed).ToString();
            return signed.ToString();
        }

        return t;
    }

    public static bool ValuesEqual(string? expected, string? current, string type)
    {
        string? e = NormalizeExpected(expected, type);
        string? c = current;
        if (e is null && c is null) return true;
        if (e is null || c is null) return false;

        // Compare as unsigned when both numeric
        if (ulong.TryParse(e, out ulong eu) && ulong.TryParse(c, out ulong cu))
            return eu == cu;

        return string.Equals(e, c, StringComparison.OrdinalIgnoreCase);
    }
}
