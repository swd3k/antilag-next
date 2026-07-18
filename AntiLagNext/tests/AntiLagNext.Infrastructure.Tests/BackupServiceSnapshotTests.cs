using AntiLagNext.Infrastructure.Safety;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

/// <summary>
/// Snapshot must never throw on missing values (Health Fix / catalog apply path).
/// </summary>
public class BackupServiceSnapshotTests
{
    private static readonly string TestKey =
        @"Software\AntiLagNext\Tests\Snapshot";

    [Fact]
    public void SnapshotCurrentRegistryValue_missing_value_does_not_throw()
    {
        // Ensure key exists without the value name
        using (var k = Registry.CurrentUser.CreateSubKey(TestKey, true))
        {
            k!.DeleteValue("MissingValue", throwOnMissingValue: false);
        }

        var backup = new BackupService();
        var session = backup.BeginSession("unit-snapshot-missing", null);

        var act = () => backup.SnapshotCurrentRegistryValue(
            session,
            Registry.CurrentUser,
            TestKey,
            "MissingValue");

        act.Should().NotThrow();

        var committed = backup.CommitSession(session);
        committed.Success.Should().BeTrue();
        committed.Value!.RegistryEntries.Should().Contain(e =>
            e.ValueName == "MissingValue" && e.WasMissing);
    }

    [Fact]
    public void SnapshotCurrentRegistryValue_missing_key_does_not_throw()
    {
        const string missingPath = @"Software\AntiLagNext\Tests\NoSuchKey_XYZ";
        try { Registry.CurrentUser.DeleteSubKeyTree(missingPath, throwOnMissingSubKey: false); }
        catch { /* ignore */ }

        var backup = new BackupService();
        var session = backup.BeginSession("unit-snapshot-missing-key", null);

        var act = () => backup.SnapshotCurrentRegistryValue(
            session,
            Registry.CurrentUser,
            missingPath,
            "Any");

        act.Should().NotThrow();
        backup.CommitSession(session).Success.Should().BeTrue();
    }

    [Fact]
    public void SnapshotCurrentRegistryValue_existing_dword_captures_value()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(TestKey, true))
        {
            k!.SetValue("PresentDword", 42, RegistryValueKind.DWord);
        }

        var backup = new BackupService();
        var session = backup.BeginSession("unit-snapshot-present", null);
        backup.SnapshotCurrentRegistryValue(session, Registry.CurrentUser, TestKey, "PresentDword");
        var rec = backup.CommitSession(session);

        rec.Success.Should().BeTrue();
        var entry = rec.Value!.RegistryEntries.Should().ContainSingle(e => e.ValueName == "PresentDword").Subject;
        entry.WasMissing.Should().BeFalse();
        entry.SerializedValue.Should().Be("42");
    }
}
