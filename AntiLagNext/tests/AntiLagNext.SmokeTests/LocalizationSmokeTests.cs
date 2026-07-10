using AntiLagNext.Infrastructure.Localization;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.SmokeTests;

public class LocalizationSmokeTests
{
    [Theory]
    [InlineData("ru", "page.dashboard.title", "Панель")]
    [InlineData("en", "page.dashboard.title", "Dashboard")]
    [InlineData("ru", "nav.profiles", "профил")]
    [InlineData("ru", "dash.toggle.parking", "ядра")]
    [InlineData("ru", "profiles.delete", "УДАЛИТЬ")]
    public void BuiltIn_Resolves_When_Disk_Missing(string culture, string key, string contains)
    {
        var missing = Path.Combine(Path.GetTempPath(), "antilag-missing-i18n-" + Guid.NewGuid().ToString("N"));
        var loc = new JsonLocalizationService(missing);
        loc.SetCulture(culture);
        var text = loc.T(key);
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeEquivalentTo(key);
        text.Should().ContainEquivalentOf(contains);
    }

    [Fact]
    public void Never_Returns_Raw_Keys_For_Chrome()
    {
        var missing = Path.Combine(Path.GetTempPath(), "antilag-missing-i18n-" + Guid.NewGuid().ToString("N"));
        var loc = new JsonLocalizationService(missing);
        loc.SetCulture("ru");
        foreach (var key in new[]
                 {
                     "page.dashboard.title", "page.profiles.title", "page.settings.title",
                     "nav.dashboard", "dash.cta.enable", "profiles.delete", "profiles.new"
                 })
        {
            var t = loc.T(key);
            t.Should().NotStartWith("page.");
            t.Should().NotStartWith("nav.");
            t.Should().NotStartWith("dash.");
            t.Should().NotStartWith("profiles.");
        }
    }

    [Fact]
    public void Disk_Pack_From_App_Source_Loads_If_Present()
    {
        // Walk up from test bin to find repo i18n
        string? dir = AppContext.BaseDirectory;
        string? found = null;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "src", "AntiLagNext.App", "i18n");
            if (File.Exists(Path.Combine(candidate, "ru.json")))
            {
                found = candidate;
                break;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        if (found is null)
        {
            // Not fatal in CI layouts without sources next to tests
            return;
        }

        var loc = new JsonLocalizationService(found);
        loc.SetCulture("ru");
        loc.T("page.dashboard.title").Should().NotBe("page.dashboard.title");
        loc.T("profiles.delete").Should().NotBe("profiles.delete");
    }
}
