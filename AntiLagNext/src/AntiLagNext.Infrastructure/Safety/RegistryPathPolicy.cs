namespace AntiLagNext.Infrastructure.Safety;

/// <summary>
/// Allowlist for registry restore from backup JSON.
/// Prevents malicious backup files from writing arbitrary HKLM keys under elevation.
/// </summary>
public static class RegistryPathPolicy
{
    /// <summary>
    /// Exact prefixes of keys AntiLag Next is allowed to restore.
    /// Intentionally narrow — no open-ended Services\ or Class\ trees.
    /// </summary>
    private static readonly string[] AllowedPrefixes =
    {
        @"SOFTWARE\Microsoft\GameBar",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR",
        @"SOFTWARE\Policies\Microsoft\Windows\GameDVR",
        @"System\GameConfigStore",
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        @"SYSTEM\CurrentControlSet\Services\nvlddmkm",
        @"SYSTEM\CurrentControlSet\Services\amdkmdag",
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
        @"SOFTWARE\NVIDIA Corporation",
        @"SOFTWARE\AMD\CN",
        @"SOFTWARE\Microsoft\DirectX",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
        @"Control Panel\Mouse",
        @"Software\AntiLagNext",
        @"SOFTWARE\AntiLagNext",
    };

    public static bool IsSafeRegistryPath(string hive, string keyPath, string valueName)
    {
        if (hive is not ("HKLM" or "HKCU"))
            return false;
        if (string.IsNullOrWhiteSpace(keyPath) || keyPath.Length > 512)
            return false;
        if (keyPath.Contains("..", StringComparison.Ordinal) || keyPath.Contains('\0'))
            return false;
        if (valueName is { Length: > 256 } || (valueName?.Contains('\0') ?? false))
            return false;

        // Normalize separators
        string path = keyPath.Replace('/', '\\').TrimStart('\\');

        // Explicit allowlist prefixes first (e.g. Tcpip\Parameters, GPU driver keys)
        if (AllowedPrefixes.Any(a => path.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Service Start-type restore: only allowlisted service names, key root only
        // (no nested Parameters/Security — those must be on AllowedPrefixes if needed)
        const string svcRoot = @"SYSTEM\CurrentControlSet\Services\";
        if (path.StartsWith(svcRoot, StringComparison.OrdinalIgnoreCase))
        {
            string rest = path[svcRoot.Length..];
            int slash = rest.IndexOf('\\');
            string serviceName = slash >= 0 ? rest[..slash] : rest;
            if (slash >= 0)
                return false; // nested under service not allowlisted
            return ServiceAllowList.IsSafe(serviceName);
        }

        return false;
    }

    /// <summary>SCM Start type must be a known SERVICE_* constant (0–4).</summary>
    public static bool IsValidServiceStartType(int startType) =>
        startType is >= 0 and <= 4;
}
