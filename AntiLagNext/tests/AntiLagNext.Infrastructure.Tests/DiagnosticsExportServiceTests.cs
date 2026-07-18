using System.IO.Compression;
using AntiLagNext.Infrastructure.Services;
using AntiLagNext.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class DiagnosticsExportServiceTests
{
    [Fact]
    public void ExportZip_creates_zip_with_meta_json()
    {
        var svc = new DiagnosticsExportService();
        string? zipPath = null;

        try
        {
            var result = svc.ExportZip(logsText: "test-log-line");
            result.Success.Should().BeTrue(because: result.Detail ?? result.Message);
            result.Value.Should().NotBeNullOrWhiteSpace();
            zipPath = result.Value!;
            File.Exists(zipPath).Should().BeTrue();

            zipPath.Should().StartWith(AppPaths.DiagnosticsDirectory);
            Path.GetFileName(zipPath).Should().MatchRegex(@"^antilag-diagnostics-\d{8}-\d{6}(-[0-9a-f]{6})?\.zip$");

            using var zip = ZipFile.OpenRead(zipPath);
            zip.GetEntry("meta.json").Should().NotBeNull("meta.json must be present");
            zip.GetEntry("settings-redacted.json").Should().NotBeNull();
            zip.GetEntry("logs-snapshot.txt").Should().NotBeNull();

            using (var stream = zip.GetEntry("meta.json")!.Open())
            using (var reader = new StreamReader(stream))
            {
                string meta = reader.ReadToEnd();
                meta.Should().Contain("appVersion");
                meta.Should().Contain("exportedUtc");
                meta.Should().Contain("osDescription");
                meta.Should().Contain("isElevated");
                meta.Should().Contain("isPortable");
            }

            using (var stream = zip.GetEntry("logs-snapshot.txt")!.Open())
            using (var reader = new StreamReader(stream))
            {
                reader.ReadToEnd().Should().Be("test-log-line");
            }
        }
        finally
        {
            if (zipPath != null)
            {
                try { File.Delete(zipPath); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void ExportZip_without_logs_writes_placeholder()
    {
        var svc = new DiagnosticsExportService();
        string? zipPath = null;

        try
        {
            var result = svc.ExportZip();
            result.Success.Should().BeTrue(because: result.Detail ?? result.Message);
            zipPath = result.Value!;

            using var zip = ZipFile.OpenRead(zipPath);
            using var stream = zip.GetEntry("logs-snapshot.txt")!.Open();
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            text.Should().Contain("Logs not provided");
            text.Should().Contain("ExportZip");
        }
        finally
        {
            if (zipPath != null)
            {
                try { File.Delete(zipPath); } catch { /* ignore */ }
            }
        }
    }
}
