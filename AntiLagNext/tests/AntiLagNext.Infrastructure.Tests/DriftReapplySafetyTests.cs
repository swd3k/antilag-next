using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Tweaks;
using FluentAssertions;
using Xunit;

namespace AntiLagNext.Infrastructure.Tests;

public class DriftReapplySafetyTests
{
    private sealed class EmptyDesired : IDesiredStateStore
    {
        public DesiredStateDocument Load() => new();
        public void Save(DesiredStateDocument document) { }
        public void Upsert(DesiredStateEntry entry) { }
        public void Clear() { }
        public IReadOnlyList<DesiredStateEntry> GetEntries() => Array.Empty<DesiredStateEntry>();
    }

    [Fact]
    public async Task Reapply_with_empty_desired_is_noop()
    {
        // RegistryTweakEngine needs backup — pass null via reflection not available.
        // DriftService.ReapplyDriftedAsync only needs desired empty path before engine.
        var drift = new DriftService(new EmptyDesired(), engine: null!);
        // Will NRE if it tries ApplyAsync — empty path must return before using engine
        var r = await drift.ReapplyDriftedAsync(Guid.NewGuid());
        r.Success.Should().BeTrue();
        r.Message.Should().Contain("no desired-state");
    }
}
