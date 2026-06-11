using MicGain.Core.IO;

namespace MicGain.Core.Tests;

/// <summary>In-memory <see cref="IProcessRunner"/> — records launches, never spawns processes (AGENTS.md rule 3).</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    /// <summary>Every launch requested by the service, in order.</summary>
    public List<(string FileName, string Arguments)> Launched { get; } = new();

    /// <summary>Exit code returned for every launch. Defaults to 0 (success).</summary>
    public int NextExitCode { get; set; }

    public Task<int> RunElevatedAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        Launched.Add((fileName, arguments));
        return Task.FromResult(NextExitCode);
    }
}