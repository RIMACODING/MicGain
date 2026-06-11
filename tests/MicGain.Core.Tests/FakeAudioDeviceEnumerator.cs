using MicGain.Core.Audio;
using MicGain.Core.Models;

namespace MicGain.Core.Tests;

/// <summary>In-memory <see cref="IAudioDeviceEnumerator"/> so tests run without Windows audio (AGENTS.md rule 3).</summary>
public sealed class FakeAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public List<AudioEndpoint> Endpoints { get; } = new();

    public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints() => Endpoints;
}
