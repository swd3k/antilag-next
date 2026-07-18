using AntiLagNext.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("https://github.com/swd3k/antilag-next/releases/tag/v1.2.0", "v1.2.0")]
    [InlineData("https://github.com/swd3k/antilag-next/releases/download/v1.2.1/x.exe", "v1.2.1")]
    [InlineData("tag:github.com,2008:Repository/1294332732/v1.2.0", "v1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    public void TryExtractTag_ok(string text, string expected)
    {
        UpdateService.TryExtractTag(text, out var tag).Should().BeTrue();
        tag.Should().Be(expected);
    }

    [Fact]
    public void TryParseLatestTagFromAtom_reads_first_entry()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>releases</title>
              <entry>
                <id>tag:github.com,2008:Repository/1294332732/v1.2.0</id>
                <link rel="alternate" type="text/html" href="https://github.com/swd3k/antilag-next/releases/tag/v1.2.0"/>
                <title>AntiLag Next v1.2.0</title>
              </entry>
              <entry>
                <id>tag:github.com,2008:Repository/1294332732/v1.1.0</id>
                <title>AntiLag Next v1.1.0</title>
              </entry>
            </feed>
            """;

        UpdateService.TryParseLatestTagFromAtom(xml, out var tag).Should().BeTrue();
        tag.Should().Be("v1.2.0");
    }

    [Fact]
    public void BuildSetupDownloadUrl_canonical()
    {
        UpdateService.BuildSetupDownloadUrl("1.2.1", "win-x64")
            .Should().Be("https://github.com/swd3k/antilag-next/releases/download/v1.2.1/AntiLagNext-Setup-1.2.1-win-x64.exe");
        UpdateService.BuildSetupAssetName("v1.2.1", "win-arm64")
            .Should().Be("AntiLagNext-Setup-1.2.1-win-arm64.exe");
    }

    [Theory]
    [InlineData("https://github.com/swd3k/antilag-next/releases/download/v1.2.1/AntiLagNext-Setup-1.2.1-win-x64.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/1/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("https://raw.githubusercontent.com/swd3k/antilag-next/main/evil.exe", false)]
    [InlineData("https://evil.githubusercontent.com/payload", false)]
    [InlineData("https://github.com/evil/repo/releases/download/v1/x.exe", false)]
    [InlineData("https://github.com/swd3k/antilag-next/archive/refs/heads/main.zip", false)]
    [InlineData("http://github.com/swd3k/antilag-next/releases/download/v1/x.exe", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public void IsAllowedDownloadUrl_policy(string url, bool ok)
    {
        UpdateService.IsAllowedDownloadUrl(url).Should().Be(ok);
    }

    [Theory]
    [InlineData("https://github.com/swd3k/antilag-next/releases/latest", true)]
    [InlineData("https://github.com/swd3k/antilag-next/releases/tag/v1.2.1", true)]
    [InlineData("https://github.com/swd3k/antilag-next", true)]
    [InlineData("https://github.com/swd3k/other/releases", false)]
    [InlineData("https://evil.com/swd3k/antilag-next/releases", false)]
    [InlineData("file:///C:/temp/x", false)]
    [InlineData("javascript:alert(1)", false)]
    public void IsAllowedReleasePageUrl_policy(string url, bool ok)
    {
        UpdateService.IsAllowedReleasePageUrl(url).Should().Be(ok);
    }

    [Fact]
    public void LooksLikePeExecutable_rejects_text()
    {
        string path = Path.Combine(Path.GetTempPath(), "antilag-pe-test-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(path, "not a pe");
            UpdateService.LooksLikePeExecutable(path).Should().BeFalse();
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ClassifyError_never_uses_raw_russian_socket_text()
    {
        var (code, msg) = UpdateService.ClassifyError(
            new HttpRequestException("Попытка установить соединение была безуспешной",
                new System.Net.Sockets.SocketException(10060)));
        code.Should().Be(UpdateService.ErrorCodes.Network);
        msg.Should().NotContain("Попытка");
        msg.Should().Contain("GitHub");

        var (c2, m2) = UpdateService.ClassifyError(new TimeoutException());
        c2.Should().Be(UpdateService.ErrorCodes.Timeout);
        m2.Should().Contain("timed out");

        // Unknown CLR exceptions still surface as actionable network copy (not "Update check failed")
        var (c3, m3) = UpdateService.ClassifyError(new InvalidOperationException("boom"));
        c3.Should().Be(UpdateService.ErrorCodes.Network);
        m3.Should().Contain("GitHub");
    }

    [Fact]
    public async Task CheckAsync_fallback_when_api_blocked_returns_result_or_english_error()
    {
        // Live network: either finds latest via Atom/redirect or returns English ErrorCode
        var svc = new UpdateService();
        var result = await svc.CheckAsync();
        result.LocalVersion.Should().NotBeNullOrWhiteSpace();
        if (!string.IsNullOrEmpty(result.Error))
        {
            result.ErrorCode.Should().NotBeNullOrEmpty();
            result.Error.Should().NotMatchRegex(@"[А-Яа-яЁё]");
        }
        else
        {
            result.LatestVersion.Should().NotBeNullOrWhiteSpace();
            // current main is at least 1.2.0
            AntiLagNext.Core.Models.SemVer.TryParse(result.LatestVersion, out var latest).Should().BeTrue();
            latest.Major.Should().BeGreaterThanOrEqualTo(1);
        }
    }
}
