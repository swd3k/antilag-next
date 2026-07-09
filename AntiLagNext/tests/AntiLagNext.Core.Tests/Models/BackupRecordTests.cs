using AntiLagNext.Core.Models;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Models;

/// <summary>
/// Тесты моделей бэкапа и преобразования единиц таймера.
/// </summary>
public class BackupRecordTests
{
    [Fact]
    public void BackupRecord_Defaults_AreSafe()
    {
        var record = new BackupRecord();

        record.RegistryEntries.Should().BeEmpty();
        record.PowerEntries.Should().BeEmpty();
        record.SystemRestorePointCreated.Should().BeFalse();
        record.OperationName.Should().BeEmpty();
    }

    [Theory]
    [InlineData(5000u,   0.5,    "минимальное разрешение 0.5 мс = 5000 единиц по 100 нс")]
    [InlineData(10000u,  1.0,    "1.0 мс = 10000 единиц")]
    [InlineData(156250u, 15.625, "стандарт 15.625 мс = 156250 единиц")]
    public void TimerCaps_ConvertsToMs_Correctly(uint period100Ns, double expectedMs, string reason)
    {
        var caps = new TimerCaps { MinimumPeriod = period100Ns, MaximumPeriod = period100Ns };

        caps.MinimumMs.Should().Be(expectedMs, reason);
    }

    [Fact]
    public void RegistryBackupEntry_ParsesHive()
    {
        var entry = new RegistryBackupEntry { Hive = "HKLM", KeyPath = @"SOFTWARE\X", ValueName = "Y" };

        // RootHive — свойство, привязанное к Microsoft.Win32; в Core доступно через тип RegistryKey.
        entry.Hive.Should().Be("HKLM");
        entry.RootHive.Should().NotBeNull("HKLM должен маппиться на Registry.LocalMachine");
    }

    [Fact]
    public void HybridCpuTopology_IsHybrid_TrueWhenEfficientCoresExist()
    {
        var hybrid = new HybridCpuTopology { LogicalProcessorCount = 20, PerformanceCoreCount = 16, EfficientCoreCount = 4 };
        var homogeneous = new HybridCpuTopology { LogicalProcessorCount = 8, PerformanceCoreCount = 8, EfficientCoreCount = 0 };

        hybrid.IsHybrid.Should().BeTrue();
        homogeneous.IsHybrid.Should().BeFalse();
    }

    [Fact]
    public void OperationResult_OkAndFail_Constructions()
    {
        var ok = OperationResult.Ok("готово");
        var fail = OperationResult.Fail("провал", "деталь");

        ok.Success.Should().BeTrue();
        fail.Success.Should().BeFalse();
        fail.Detail.Should().Be("деталь");
    }
}
