namespace AntiLagNext.Infrastructure.Safety;

/// <summary>
/// Safe service names that ServiceOptimizer may disable/manual and BackupService may restore.
/// Never includes boot-critical SCM services.
/// </summary>
public static class ServiceAllowList
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // Superfetch / SysMain — common gaming tweak, reversible
        "SysMain",
        // DiagTrack telemetry
        "DiagTrack",
        // Windows Search (optional; can hurt desktop search)
        "WSearch",
        // Xbox services (not required for offline games)
        "XblAuthManager",
        "XblGameSave",
        "XboxGipSvc",
        "XboxNetApiSvc",
        // Remote registry
        "RemoteRegistry",
        // Fax
        "Fax",
    };

    public static bool IsSafe(string serviceName) =>
        !string.IsNullOrWhiteSpace(serviceName) && Allowed.Contains(serviceName.Trim());

    public static IReadOnlyCollection<string> All => Allowed;
}
