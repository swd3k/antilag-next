using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Storage;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Загрузка/сохранение AppSettings в %APPDATA%\AntiLagNext\settings\user-settings.json.
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
                return OperationResult.Ok("Созданы настройки по умолчанию.");
            }

            // Гарантируем наличие трёх пресетов
            EnsurePresets(loaded);
            _current = loaded;
            return OperationResult.Ok("Настройки загружены.");
        }
        catch (Exception ex)
        {
            _current = AppSettings.CreateDefault();
            return OperationResult.Fail("Ошибка загрузки настроек — используются значения по умолчанию.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            JsonStorage.Save(AppPaths.SettingsFile, _current);
            return OperationResult.Ok("Настройки сохранены.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось сохранить настройки.", detail: ex.Message, ex: ex);
        }
    }

    private static void EnsurePresets(AppSettings s)
    {
        if (!s.Profiles.Any(p => p.Kind == Core.Enums.ProfileKind.Default))
            s.Profiles.Insert(0, OptimizationProfile.CreatePreset(Core.Enums.ProfileKind.Default));
        if (!s.Profiles.Any(p => p.Kind == Core.Enums.ProfileKind.Gaming))
            s.Profiles.Add(OptimizationProfile.CreatePreset(Core.Enums.ProfileKind.Gaming));
        if (!s.Profiles.Any(p => p.Kind == Core.Enums.ProfileKind.Office))
            s.Profiles.Add(OptimizationProfile.CreatePreset(Core.Enums.ProfileKind.Office));
    }
}
