using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Storage;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Load/save AppSettings under %APPDATA%\AntiLagNext\settings\user-settings.json.
/// Runs schema migrations (e.g. legacy RU profile names → English stable labels).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private AppSettings _current = AppSettings.CreateDefault();

    public AppSettings Current => _current;

    public OperationResult Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            var loaded = JsonStorage.Load<AppSettings>(AppPaths.SettingsFile);
            if (loaded == null || loaded.Profiles.Count == 0)
            {
                _current = AppSettings.CreateDefault();
                Save();
                return OperationResult.Ok("Created default settings.");
            }

            bool dirty = loaded.MigrateToCurrentSchema();
            _current = loaded;
            if (dirty)
            {
                var save = Save();
                if (!save.Success)
                    return OperationResult.Ok("Settings loaded; migration save deferred: " + save.Message);
                return OperationResult.Ok("Settings loaded and migrated.");
            }

            return OperationResult.Ok("Settings loaded.");
        }
        catch (Exception ex)
        {
            _current = AppSettings.CreateDefault();
            return OperationResult.Fail(
                "Settings load failed — using defaults.",
                detail: ex.Message,
                ex: ex);
        }
    }

    public OperationResult Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            // Keep built-ins normalized on every save
            _current.MigrateToCurrentSchema();
            JsonStorage.Save(AppPaths.SettingsFile, _current);
            return OperationResult.Ok("Settings saved.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Could not save settings.", detail: ex.Message, ex: ex);
        }
    }
}
