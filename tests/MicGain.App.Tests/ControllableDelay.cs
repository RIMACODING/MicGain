namespace MicGain.App.Tests;

/// <summary>
/// Deterministic stand-in for Task.Delay in debounce tests: each delay completes only when
/// released by the test, and honors cancellation (so superseded debounces cancel exactly
/// like production).
/// </summary>
public sealed class ControllableDelay
{
    private readonly List<TaskCompletionSource> _pending = new();

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        lock (_pending)
        {
            _pending.Add(tcs);
        }

        return tcs.Task;
    }

    /// <summary>Completes all pending delays; already-cancelled ones are unaffected.</summary>
    public void ReleaseAll()
    {
        lock (_pending)
        {
            foreach (var tcs in _pending)
            {
                tcs.TrySetResult();
            }

            _pending.Clear();
        }
    }
}
