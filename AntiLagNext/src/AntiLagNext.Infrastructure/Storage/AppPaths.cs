using AntiLagNext.Core.Abstractions;

namespace AntiLagNext.Infrastructure.Storage;

/// <summary>
/// Централизованное управление путями к данным приложения.
/// Всё хранится в %APPDATA%\AntiLagNext (Portable-friendly: рядом с exe при наличии AntiLagNext.portable).
/// </summary>
public static class AppPaths
{
    /// <summary>Корневой каталог данных.</summary>
    public static string AppDataRoot { get; } = ComputeAppDataRoot();

    /// <summary>Каталог настроек и профилей.</summary>
    public static string SettingsDirectory { get; } = Path.Combine(AppDataRoot, "settings");

    /// <summary>Файл настроек пользователя.</summary>
    public static string SettingsFile { get; } = Path.Combine(SettingsDirectory, "user-settings.json");

    /// <summary>Каталог бэкапов.</summary>
    public static string BackupDirectory { get; } = Path.Combine(AppDataRoot, "backup");

    /// <summary>Каталог логов Serilog.</summary>
    public static string LogsDirectory { get; } = Path.Combine(AppDataRoot, "logs");

    /// <summary>Файл состояния «оптимизации активны».</summary>
    public static string ActiveStateFile { get; } = Path.Combine(AppDataRoot, "active-state.json");

    /// <summary>Desired registry tweak state for drift detection.</summary>
    public static string DesiredStateFile { get; } = Path.Combine(AppDataRoot, "desired_state.json");

    /// <summary>
    /// Флаг незавершённого apply: если файл есть после краша — следующий старт откатывает.
    /// </summary>
    public static string IncompleteApplyFile { get; } = Path.Combine(AppDataRoot, "incomplete-apply.json");

    /// <summary>Exported diagnostics zip packages (local troubleshooting only).</summary>
    public static string DiagnosticsDirectory { get; } = Path.Combine(AppDataRoot, "diagnostics");

    /// <summary>External plugins: {exe}/plugins/*.dll</summary>
    public static string PluginsDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>Language packs: {exe}/i18n/*.json</summary>
    public static string I18nDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "i18n");

    /// <summary>Создать все каталоги, если их нет.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
        Directory.CreateDirectory(PluginsDirectory);
        Directory.CreateDirectory(I18nDirectory);
    }

    /// <summary>
    /// В portable-режиме (файл AntiLagNext.portable рядом с exe) данные хранятся в подкаталоге data.
    /// Иначе — в %APPDATA%\AntiLagNext.
    /// </summary>
    private static string ComputeAppDataRoot()
    {
        string portableMarker = Path.Combine(AppContext.BaseDirectory, "AntiLagNext.portable");
        if (File.Exists(portableMarker))
        {
            return Path.Combine(AppContext.BaseDirectory, "data");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AntiLagNext");
    }
}
