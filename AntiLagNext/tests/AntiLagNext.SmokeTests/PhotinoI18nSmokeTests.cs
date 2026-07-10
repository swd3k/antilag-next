using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.SmokeTests;

/// <summary>
/// Photino UI language packs must keep profile labels culture-correct
/// (regression: Active profile card showed "Игровой" on EN).
/// </summary>
public class PhotinoI18nSmokeTests
{
    private static string? FindUiI18nDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "src", "AntiLagNext.Ui", "wwwroot", "i18n");
            if (File.Exists(Path.Combine(candidate, "en.json"))
                && File.Exists(Path.Combine(candidate, "ru.json")))
                return candidate;

            // From test bin: walk up to repo AntiLagNext/
            candidate = Path.Combine(dir, "AntiLagNext", "src", "AntiLagNext.Ui", "wwwroot", "i18n");
            if (File.Exists(Path.Combine(candidate, "en.json")))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static Dictionary<string, string> LoadPack(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }

    [Fact]
    public void EnPack_ActiveProfile_IsNotRussian()
    {
        var root = FindUiI18nDir();
        if (root is null)
        {
            // CI layout without sources next to tests — skip soft
            return;
        }

        var en = LoadPack(Path.Combine(root, "en.json"));
        en.Should().ContainKey("profile.gaming");
        en.Should().ContainKey("metric.profile");
        en["profile.gaming"].Should().Be("Gaming");
        en["profile.gaming"].Should().NotBe("Игровой");
        en["metric.profile"].Should().Be("Active profile");
        en["metric.profile"].Should().NotContain("Активный");

        foreach (var key in new[] { "profile.gaming", "profile.office", "profile.max", "profile.default" })
        {
            en[key].Should().NotMatchRegex(@"[А-Яа-яЁё]",
                $"{key} must not contain Cyrillic in en.json");
        }
    }

    [Fact]
    public void RuPack_HasRussianProfileLabels()
    {
        var root = FindUiI18nDir();
        if (root is null) return;

        var ru = LoadPack(Path.Combine(root, "ru.json"));
        ru["profile.gaming"].Should().Be("Игровой");
        ru["metric.profile"].Should().Contain("профиль");
    }

    [Fact]
    public void EnAndRu_ShareProfileKeys()
    {
        var root = FindUiI18nDir();
        if (root is null) return;

        var en = LoadPack(Path.Combine(root, "en.json"));
        var ru = LoadPack(Path.Combine(root, "ru.json"));
        foreach (var key in new[]
                 {
                     "profile.gaming", "profile.office", "profile.max", "profile.default",
                     "metric.profile", "power.gaming", "power.office"
                 })
        {
            en.Should().ContainKey(key);
            ru.Should().ContainKey(key);
            en[key].Should().NotBe(ru[key], $"{key} should differ between en and ru");
        }
    }
}
