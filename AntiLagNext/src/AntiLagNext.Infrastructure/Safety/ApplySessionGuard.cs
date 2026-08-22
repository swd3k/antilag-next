using System.Text.Json;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Storage;

namespace AntiLagNext.Infrastructure.Safety;

/// <summary>
/// Crash-recovery: marks apply as incomplete until commit succeeds.
/// On next startup, host should call <see cref="RecoverIfNeededAsync"/>.
/// </summary>
public static class ApplySessionGuard
{
    private sealed class IncompleteMarker
    {
        public Guid SessionId { get; set; }
        public string OperationName { get; set; } = string.Empty;
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    }

    public static void MarkBegin(Guid sessionId, string operationName)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var marker = new IncompleteMarker
            {
                SessionId = sessionId,
                OperationName = operationName,
                StartedUtc = DateTime.UtcNow
            };
            File.WriteAllText(AppPaths.IncompleteApplyFile,
                JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            /* best-effort */
        }
    }

    public static void MarkComplete()
    {
        try
        {
            if (File.Exists(AppPaths.IncompleteApplyFile))
                File.Delete(AppPaths.IncompleteApplyFile);
        }
        catch
        {
            /* best-effort */
        }
    }

    public static bool HasIncompleteApply() => File.Exists(AppPaths.IncompleteApplyFile);

    /// <summary>
    /// If previous process crashed mid-apply, restore via SafetyService.ResetAll.
    /// </summary>
    public static async Task<OperationResult?> RecoverIfNeededAsync(
        ISafetyService safety,
        CancellationToken cancellationToken = default)
    {
        if (!HasIncompleteApply())
            return null;

        try
        {
            Guid? sessionId = TryReadSessionId();
            if (sessionId is not Guid sid || sid == Guid.Empty)
            {
                MarkComplete();
                return OperationResult.Fail("Crash recovery: incomplete marker invalid — skipped registry restore.");
            }

            var reset = await safety.ResetAllAsync(cancellationToken, sid).ConfigureAwait(false);
            MarkComplete();
            return reset.Success
                ? OperationResult.Ok("Crash recovery: incomplete apply rolled back. " + reset.Message)
                : OperationResult.Fail("Crash recovery failed: " + reset.Message, detail: reset.Detail);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Crash recovery error.", detail: ex.Message, ex: ex);
        }
    }

    private static Guid? TryReadSessionId()
    {
        try
        {
            string json = File.ReadAllText(AppPaths.IncompleteApplyFile);
            var marker = JsonSerializer.Deserialize<IncompleteMarker>(json);
            if (marker is null || marker.SessionId == Guid.Empty)
                return null;
            return marker.SessionId;
        }
        catch
        {
            return null;
        }
    }
}
