using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Safety;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Tweaks;

/// <summary>
/// Applies catalog registry tweaks with path policy, backup snapshots, and desired-state upsert.
/// </summary>
public sealed class RegistryTweakEngine
{
    private readonly IBackupService _backup;
    private readonly IDesiredStateStore _desiredState;

    public RegistryTweakEngine(IBackupService backup, IDesiredStateStore desiredState)
    {
        _backup = backup;
        _desiredState = desiredState;
    }

    /// <summary>
    /// Apply a batch of tweak definitions under an open backup session.
    /// </summary>
    public Task<OperationResult> ApplyAsync(
        IReadOnlyList<TweakDefinition> definitions,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (definitions == null || definitions.Count == 0)
            return Task.FromResult(OperationResult.Ok("Latency tweaks: nothing to apply."));

        var messages = new List<string>();
        var errors = new List<string>();
        int applied = 0;

        foreach (var def in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var one = ApplyOne(def, sessionId);
                if (one.Success)
                {
                    applied++;
                    messages.Add(one.Message);
                }
                else
                {
                    errors.Add($"{def.Id}: {one.Message}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{def.Id}: {ex.Message}");
            }
        }

        string? detail = errors.Count > 0 ? string.Join("; ", errors) : null;

        if (errors.Count == 0)
            return Task.FromResult(OperationResult.Ok(
                $"Latency tweaks: {applied}/{definitions.Count} applied."));

        // Partial apply is still useful for Health Fix — don't hard-fail if something landed.
        if (applied > 0)
        {
            return Task.FromResult(new OperationResult
            {
                Success = true,
                Message = $"Latency tweaks: {applied}/{definitions.Count} applied (some skipped).",
                Detail = detail
            });
        }

        return Task.FromResult(OperationResult.Fail(
            "Latency tweaks failed.",
            detail: detail));
    }

    private OperationResult ApplyOne(TweakDefinition def, Guid sessionId)
    {
        if (!RegistryPathPolicy.IsSafeRegistryPath(def.Hive, def.KeyPath, def.ValueName))
            return OperationResult.Fail($"Path not allowlisted: {def.Hive}\\{def.KeyPath}\\{def.ValueName}");

        var root = ResolveHive(def.Hive);
        if (root is null)
            return OperationResult.Fail($"Unknown hive: {def.Hive}");

        object regValue = CoerceValue(def.DesiredValue, def.ValueKind);
        var kind = (RegistryValueKind)def.ValueKind;

        try
        {
            SnapshotBefore(sessionId, root, def.Hive, def.KeyPath, def.ValueName);
        }
        catch (Exception ex)
        {
            // Snapshot is best-effort — never block apply (was: GetValueKind on missing name).
            System.Diagnostics.Trace.TraceWarning(
                "Snapshot skipped for {0}\\{1}: {2}", def.KeyPath, def.ValueName, ex.Message);
        }

        try
        {
            using var key = root.CreateSubKey(def.KeyPath, writable: true)
                ?? throw new InvalidOperationException($"Cannot open key {def.KeyPath}");

            // If an existing value has a different kind, delete then rewrite (String vs DWord, etc.)
            try
            {
                object? existing = key.GetValue(def.ValueName, null);
                if (existing != null)
                {
                    RegistryValueKind existingKind;
                    try { existingKind = key.GetValueKind(def.ValueName); }
                    catch { existingKind = RegistryValueKind.Unknown; }

                    if (existingKind != RegistryValueKind.Unknown && existingKind != kind)
                        key.DeleteValue(def.ValueName, throwOnMissingValue: false);
                }
            }
            catch { /* continue with SetValue */ }

            key.SetValue(def.ValueName, regValue, kind);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail($"Access denied (run as Administrator): {def.Id}", detail: ex.Message, ex: ex);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Write failed: {def.Id}", detail: ex.Message, ex: ex);
        }

        try
        {
            _desiredState.Upsert(new DesiredStateEntry
            {
                TweakId = def.Id,
                Hive = def.Hive,
                Path = def.KeyPath,
                Name = def.ValueName,
                Type = TweakValueCodec.TypeName(def.ValueKind),
                Expected = TweakValueCodec.Serialize(def.DesiredValue),
                Category = def.CategoryId
            });
        }
        catch (Exception ex)
        {
            // Value is set; desired-state is secondary
            System.Diagnostics.Trace.TraceWarning("DesiredState upsert failed for {0}: {1}", def.Id, ex.Message);
        }

        string reboot = def.RequiresReboot ? " (reboot may be required)" : "";
        return OperationResult.Ok($"{def.Id} set{reboot}");
    }

    private void SnapshotBefore(Guid sessionId, RegistryKey root, string hive, string keyPath, string valueName)
    {
        if (sessionId == Guid.Empty) return;

        if (_backup is BackupService bs)
        {
            bs.SnapshotCurrentRegistryValue(sessionId, root, keyPath, valueName);
            return;
        }

        try
        {
            using var key = root.OpenSubKey(keyPath, writable: false);
            object? existing = key?.GetValue(valueName, null);
            var kind = RegistryValueKind.Unknown;
            if (existing != null && key != null)
            {
                try { kind = key.GetValueKind(valueName); }
                catch { kind = RegistryValueKind.DWord; }
            }

            _backup.SnapshotRegistryValue(sessionId, new RegistryBackupEntry
            {
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                ValueKind = (int)kind,
                SerializedValue = existing?.ToString(),
                WasMissing = existing == null
            });
        }
        catch
        {
            /* best-effort snapshot */
        }
    }

    internal static RegistryKey? ResolveHive(string hive) => hive switch
    {
        "HKLM" => Registry.LocalMachine,
        "HKCU" => Registry.CurrentUser,
        _ => null
    };

    internal static object CoerceValue(object? desired, int valueKind)
    {
        if (desired is null)
            return 0;

        if (valueKind == TweakValueCodec.KindString)
            return desired.ToString() ?? string.Empty;

        // DWord / QWord numeric
        if (desired is int i) return i;
        if (desired is long l)
            return valueKind == TweakValueCodec.KindQWord ? l : unchecked((int)l);
        if (desired is uint u) return unchecked((int)u);
        if (desired is ulong ul) return unchecked((int)ul);

        string s = desired.ToString() ?? "0";
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(s.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint hex))
            return unchecked((int)hex);

        if (int.TryParse(s, out int parsed))
            return parsed;

        if (long.TryParse(s, out long pl))
            return unchecked((int)pl);

        return 0;
    }
}
