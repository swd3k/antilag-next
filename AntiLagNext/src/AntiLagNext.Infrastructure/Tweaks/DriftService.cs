using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Tweaks;

/// <summary>
/// Compares <see cref="IDesiredStateStore"/> / catalog expected values vs live registry.
/// </summary>
public sealed class DriftService : IDriftService
{
    private readonly IDesiredStateStore _desiredState;
    private readonly RegistryTweakEngine _engine;

    public DriftService(IDesiredStateStore desiredState, RegistryTweakEngine engine)
    {
        _desiredState = desiredState;
        _engine = engine;
    }

    public IReadOnlyList<DriftEntry> Scan()
    {
        var results = new List<DriftEntry>();

        // Prefer persisted desired state; fall back to full catalog expectations
        var entries = _desiredState.GetEntries();
        if (entries.Count == 0)
        {
            foreach (var def in TweakCatalog.All)
            {
                results.Add(ReadLive(
                    def.Id,
                    def.Hive,
                    def.KeyPath,
                    def.ValueName,
                    TweakValueCodec.TypeName(def.ValueKind),
                    TweakValueCodec.Serialize(def.DesiredValue)));
            }

            return results;
        }

        foreach (var e in entries)
        {
            results.Add(ReadLive(e.TweakId, e.Hive, e.Path, e.Name, e.Type, e.Expected));
        }

        return results;
    }

    public async Task<OperationResult> ReapplyDriftedAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var drifted = Scan()
            .Where(d => d.Status is DriftStatus.Drifted or DriftStatus.Missing)
            .ToList();

        if (drifted.Count == 0)
            return OperationResult.Ok("Drift: nothing to reapply.");

        var defs = new List<TweakDefinition>();
        foreach (var d in drifted)
        {
            var def = TweakCatalog.GetById(d.TweakId);
            if (def != null)
                defs.Add(def);
        }

        if (defs.Count == 0)
            return OperationResult.Fail("Drift: drifted entries not found in catalog.");

        return await _engine.ApplyAsync(defs, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private static DriftEntry ReadLive(
        string tweakId,
        string hive,
        string path,
        string name,
        string type,
        string? expected)
    {
        try
        {
            var root = RegistryTweakEngine.ResolveHive(hive);
            if (root is null)
            {
                return new DriftEntry
                {
                    TweakId = tweakId,
                    Status = DriftStatus.Missing,
                    Current = null,
                    Expected = expected,
                    Path = path,
                    Name = name,
                    Hive = hive
                };
            }

            using var key = root.OpenSubKey(path, writable: false);
            var live = key?.GetValue(name);
            if (live is null)
            {
                return new DriftEntry
                {
                    TweakId = tweakId,
                    Status = DriftStatus.Missing,
                    Current = null,
                    Expected = expected,
                    Path = path,
                    Name = name,
                    Hive = hive
                };
            }

            string? current = TweakValueCodec.NormalizeLive(live);
            bool ok = TweakValueCodec.ValuesEqual(expected, current, type);
            return new DriftEntry
            {
                TweakId = tweakId,
                Status = ok ? DriftStatus.Ok : DriftStatus.Drifted,
                Current = current,
                Expected = expected,
                Path = path,
                Name = name,
                Hive = hive
            };
        }
        catch (Exception ex)
        {
            return new DriftEntry
            {
                TweakId = tweakId,
                Status = DriftStatus.Missing,
                Current = null,
                Expected = expected + $" (read error: {ex.Message})",
                Path = path,
                Name = name,
                Hive = hive
            };
        }
    }
}
