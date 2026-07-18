using System.Text.Json;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Storage;

/// <summary>
/// Thread-safe JSON store for desired registry tweak state
/// (%AppData%\AntiLagNext\desired_state.json).
/// </summary>
public sealed class DesiredStateStore : IDesiredStateStore
{
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DesiredStateDocument Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(AppPaths.DesiredStateFile))
                    return new DesiredStateDocument();

                var json = File.ReadAllText(AppPaths.DesiredStateFile);
                return JsonSerializer.Deserialize<DesiredStateDocument>(json, JsonOpts)
                       ?? new DesiredStateDocument();
            }
            catch
            {
                return new DesiredStateDocument();
            }
        }
    }

    public void Save(DesiredStateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_lock)
        {
            try
            {
                AppPaths.EnsureDirectories();
                document.UpdatedUtc = DateTime.UtcNow;
                string tmp = AppPaths.DesiredStateFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(document, JsonOpts));
                if (File.Exists(AppPaths.DesiredStateFile))
                    File.Replace(tmp, AppPaths.DesiredStateFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, AppPaths.DesiredStateFile);
            }
            catch
            {
                /* best-effort persist */
            }
        }
    }

    public void Upsert(DesiredStateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            var doc = LoadUnlocked();
            var existing = doc.Entries.FirstOrDefault(e =>
                e.TweakId.Equals(entry.TweakId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Hive = entry.Hive;
                existing.Path = entry.Path;
                existing.Name = entry.Name;
                existing.Type = entry.Type;
                existing.Expected = entry.Expected;
                existing.Category = entry.Category;
            }
            else
            {
                doc.Entries.Add(entry);
            }

            SaveUnlocked(doc);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            SaveUnlocked(new DesiredStateDocument());
        }
    }

    public IReadOnlyList<DesiredStateEntry> GetEntries()
    {
        lock (_lock)
        {
            return LoadUnlocked().Entries.ToList();
        }
    }

    private DesiredStateDocument LoadUnlocked()
    {
        try
        {
            if (!File.Exists(AppPaths.DesiredStateFile))
                return new DesiredStateDocument();

            var json = File.ReadAllText(AppPaths.DesiredStateFile);
            return JsonSerializer.Deserialize<DesiredStateDocument>(json, JsonOpts)
                   ?? new DesiredStateDocument();
        }
        catch
        {
            return new DesiredStateDocument();
        }
    }

    private void SaveUnlocked(DesiredStateDocument document)
    {
        try
        {
            AppPaths.EnsureDirectories();
            document.UpdatedUtc = DateTime.UtcNow;
            string tmp = AppPaths.DesiredStateFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(document, JsonOpts));
            if (File.Exists(AppPaths.DesiredStateFile))
                File.Replace(tmp, AppPaths.DesiredStateFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, AppPaths.DesiredStateFile);
        }
        catch
        {
            /* best-effort */
        }
    }
}
