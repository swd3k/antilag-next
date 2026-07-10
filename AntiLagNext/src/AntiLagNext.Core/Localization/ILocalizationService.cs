namespace AntiLagNext.Core.Localization;

/// <summary>Подключаемые языковые пакеты (JSON). UI не хардкодит строки.</summary>
public interface ILocalizationService
{
    string CurrentCulture { get; }

    IReadOnlyList<string> AvailableCultures { get; }

    event EventHandler? CultureChanged;

    void SetCulture(string culture);

    /// <summary>Translate key; missing → key itself.</summary>
    string T(string key);

    /// <summary>Translate with string.Format args.</summary>
    string Tf(string key, params object[] args);
}
