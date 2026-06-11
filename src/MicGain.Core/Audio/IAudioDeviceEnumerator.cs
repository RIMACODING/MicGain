using MicGain.Core.Models;

namespace MicGain.Core.Audio;

/// <summary>
/// Wraps NAudio / Core Audio endpoint enumeration so <see cref="Services.AudioDeviceService"/>
/// is unit-testable without Windows audio (AGENTS.md rules 3 and 6 — CI has no audio stack).
/// </summary>
public interface IAudioDeviceEnumerator
{
    /// <summary>Active render and capture endpoints with their full Core Audio IDs.</summary>
    IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints();
}
