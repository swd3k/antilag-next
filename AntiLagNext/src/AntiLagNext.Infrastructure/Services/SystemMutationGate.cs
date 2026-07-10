namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Serializes system-mutating operations (profile apply, reset, registry/power tweaks)
/// so concurrent UI/auto-switch paths cannot interleave.
/// Supports re-entrancy on the same async flow (AsyncLocal depth) to avoid deadlock
/// when e.g. ProfileService.RevertAsync delegates into SafetyService.ResetAllAsync.
/// </summary>
public sealed class SystemMutationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<int> _depth = new();

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        bool acquired = false;
        if (_depth.Value == 0)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
        }

        _depth.Value++;
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _depth.Value--;
            if (acquired)
                _gate.Release();
        }
    }

    public Task RunAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }
}
