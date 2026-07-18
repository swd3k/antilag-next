using System.Runtime.InteropServices;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Plugins;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Native;
using AntiLagNext.Infrastructure.Services;
using AntiLagNext.Infrastructure.Storage;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Safety;

/// <summary>
/// Безопасность: restore point + backup session + полный Reset (timer, backup, plugins, active-state).
/// </summary>
public sealed class SafetyService : ISafetyService
{
    private readonly IBackupService _backup;
    private readonly ITimerManager _timer;
    private readonly IPowerManager _power;
    private readonly IPluginCatalog _plugins;
    private readonly AppSettings _settings;
    private readonly SystemMutationGate _mutationGate;
    private DateTime _lastRestorePointUtc = DateTime.MinValue;
    private long _lastSequenceNumber;

    public SafetyService(
        IBackupService backup,
        ITimerManager timer,
        IPowerManager power,
        IPluginCatalog plugins,
        AppSettings settings,
        SystemMutationGate mutationGate)
    {
        _backup = backup;
        _timer = timer;
        _power = power;
        _plugins = plugins;
        _settings = settings;
        _mutationGate = mutationGate;
    }

    public async Task<OperationResult<Guid>> BeforeChangesAsync(string operationName, CancellationToken cancellationToken = default)
    {
        try
        {
            bool rpCreated = false;
            string? rpError = null;
            if (_settings.CreateRestorePoint)
            {
                var rp = TryCreateRestorePoint(operationName);
                rpCreated = rp.success;
                rpError = rp.error;
            }

            var activeScheme = _power.GetActiveScheme();
            string? schemeBefore = activeScheme.Success ? activeScheme.Value.ToString() : null;
            var sessionId = _backup.BeginSession(operationName, schemeBefore);
            _backup.SetRestorePointStatus(sessionId, rpCreated, rpError);

            await Task.CompletedTask;
            return OperationResult<Guid>.Ok(sessionId, "Safety backup prepared.");
        }
        catch (Exception ex)
        {
            return OperationResult<Guid>.Fail("Could not prepare safety backup.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult CommitChanges(Guid sessionId)
    {
        var result = _backup.CommitSession(sessionId);
        return result.Success
            ? OperationResult.Ok($"Changes committed. {result.Message}")
            : OperationResult.Fail("Could not commit backup.", detail: result.Detail);
    }

    public Task<OperationResult> ResetAllAsync(CancellationToken cancellationToken = default)
        => _mutationGate.RunAsync(() => ResetAllCoreAsync(cancellationToken), cancellationToken);

    private async Task<OperationResult> ResetAllCoreAsync(CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        var errors = new List<string>();

        try
        {
            ApplySessionGuard.MarkComplete();

            // 1. Extension plugins first (MMCSS, mouse accel, process prio)
            try
            {
                var pr = await _plugins.RevertAllExtensionsAsync(cancellationToken).ConfigureAwait(false);
                if (pr.Success) messages.Add(pr.Message);
                else errors.Add(pr.Message);
            }
            catch (Exception ex)
            {
                errors.Add("Plugins: " + ex.Message);
            }

            // 2. Timer release (always)
            try
            {
                var rel = _timer.Release();
                if (rel.Success) messages.Add(rel.Message);
                else errors.Add("Timer: " + rel.Message);
            }
            catch (Exception ex)
            {
                errors.Add("Timer: " + ex.Message);
            }

            // 3. Restore last JSON backup (registry + power + original scheme)
            bool restoredFromBackup = false;
            try
            {
                var latest = _backup.LoadLatest();
                if (latest.Success && latest.Value != null)
                {
                    var restore = await _backup.RestoreAsync(latest.Value, cancellationToken).ConfigureAwait(false);
                    if (restore.Success)
                    {
                        messages.Add(restore.Message);
                        restoredFromBackup = true;
                    }
                    else
                    {
                        errors.Add(restore.Message + (restore.Detail is { } d ? " · " + d : ""));
                        // Partial restore still counts as attempt
                        restoredFromBackup = true;
                    }
                }
                else
                {
                    messages.Add("JSON backup not found — soft fallback.");
                }
            }
            catch (Exception ex)
            {
                errors.Add("Backup restore: " + ex.Message);
            }

            // 4. If no scheme restored — Balanced (safe default)
            if (!restoredFromBackup)
            {
                var setActive = _power.SetActiveScheme(PowerGuids.SchemeBalanced);
                if (setActive.Success) messages.Add("Scheme: Balanced");
                else errors.Add(setActive.Message);
            }
            else
            {
                // Ensure ActiveSchemeGuidBefore was applied; if backup had no scheme field, leave as-is
                var latest = _backup.LoadLatest();
                if (latest.Success && latest.Value is { ActiveSchemeGuidBefore: { Length: > 0 } schemeStr }
                    && Guid.TryParse(schemeStr, out var scheme))
                {
                    var set = _power.SetActiveScheme(scheme);
                    if (set.Success) messages.Add("Active power scheme restored");
                    else errors.Add("Scheme: " + set.Message);
                }
            }

            // 5. End restore point transaction
            TryEndRestorePoint();

            // 6. Clear "we applied optimizations" flag (UI must not treat stock HP plan as "our" state)
            ActiveStateStore.MarkInactive();

            cancellationToken.ThrowIfCancellationRequested();

            string summary = errors.Count == 0
                ? "All optimizations reset (timer, backup, plugins)."
                : "Reset completed partially.";

            if (messages.Count > 0)
                summary += " " + string.Join(" · ", messages.Take(6));

            return errors.Count == 0
                ? OperationResult.Ok(summary)
                : OperationResult.Fail(summary, detail: string.Join("; ", errors));
        }
        catch (Exception ex)
        {
            ActiveStateStore.MarkInactive();
            return OperationResult.Fail("Optimization reset failed.", detail: ex.Message, ex: ex);
        }
    }

    private (bool success, string? error) TryCreateRestorePoint(string description)
    {
        if ((DateTime.UtcNow - _lastRestorePointUtc).TotalSeconds < 10)
            return (false, "Restore point rate limit.");

        if (!IsSystemRestoreEnabled())
            return (false, "System Restore is disabled.");

        try
        {
            var info = new SrClient.RESTOREPOINTINFO
            {
                dwEventType = SrClient.BeginSystemChange,
                dwRestorePtType = SrClient.ModifySettings,
                llSequenceNumber = 0,
                szDescription = TruncateDescription(description)
            };

            if (!SrClient.SRSetRestorePointW(ref info, out var status))
            {
                int err = Marshal.GetLastWin32Error();
                _lastRestorePointUtc = DateTime.UtcNow;
                return (false, $"SRSetRestorePointW: {Win32Result.FormatMessage(err)} (status {status.nStatus})");
            }

            _lastRestorePointUtc = DateTime.UtcNow;
            _lastSequenceNumber = status.llSequenceNumber;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void TryEndRestorePoint()
    {
        if (_lastSequenceNumber == 0) return;
        try
        {
            var info = new SrClient.RESTOREPOINTINFO
            {
                dwEventType = SrClient.EndSystemChange,
                dwRestorePtType = SrClient.ModifySettings,
                llSequenceNumber = _lastSequenceNumber,
                szDescription = string.Empty
            };
            SrClient.SRSetRestorePointW(ref info, out _);
            _lastSequenceNumber = 0;
        }
        catch { /* best-effort */ }
    }

    private static bool IsSystemRestoreEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: false);
            if (key?.GetValue("RPSessionInterval") is int)
                return true;
            using var cfg = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", writable: false);
            if (cfg?.GetValue("DisableConfig") is int disabled)
                return disabled == 0;
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static string TruncateDescription(string description)
        => description.Length <= 256 ? description : description[..256];
}
