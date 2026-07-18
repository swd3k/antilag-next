using System.Text.Json;
using AntiLagNext.Core.Models;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Core.Tests.Models;

public class TweakModelsTests
{
    [Fact]
    public void DesiredStateDocument_roundtrips_json()
    {
        var doc = new DesiredStateDocument
        {
            Version = 1,
            UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Entries =
            {
                new DesiredStateEntry
                {
                    TweakId = "network.throttling_index",
                    Hive = "HKLM",
                    Path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    Name = "NetworkThrottlingIndex",
                    Type = "DWord",
                    Expected = "-1",
                    Category = "network"
                }
            }
        };

        string json = JsonSerializer.Serialize(doc);
        var loaded = JsonSerializer.Deserialize<DesiredStateDocument>(json);

        loaded.Should().NotBeNull();
        loaded!.Entries.Should().HaveCount(1);
        loaded.Entries[0].TweakId.Should().Be("network.throttling_index");
        loaded.Entries[0].Expected.Should().Be("-1");
    }

    [Theory]
    [InlineData("-1", "4294967295", "DWord", true)]
    [InlineData("0xFFFFFFFF", "4294967295", "DWord", true)]
    [InlineData("36", "36", "DWord", true)]
    [InlineData("0x24", "36", "DWord", true)]
    [InlineData("20", "40", "DWord", false)]
    [InlineData("High", "High", "String", true)]
    public void TweakValueCodec_ValuesEqual(string expected, string current, string type, bool eq)
    {
        TweakValueCodec.ValuesEqual(expected, current, type).Should().Be(eq);
    }

    [Fact]
    public void TweakValueCodec_Serialize_handles_primitives()
    {
        TweakValueCodec.Serialize(1).Should().Be("1");
        TweakValueCodec.Serialize(unchecked((int)0xFFFFFFFFu)).Should().Be("-1");
        TweakValueCodec.Serialize("abc").Should().Be("abc");
    }
}
