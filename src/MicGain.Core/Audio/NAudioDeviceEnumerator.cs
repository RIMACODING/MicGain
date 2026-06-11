using System.Runtime.InteropServices;
using MicGain.Core.Models;
using NAudio.CoreAudioApi;

namespace MicGain.Core.Audio;

/// <summary>
/// Production <see cref="IAudioDeviceEnumerator"/> backed by NAudio's
/// <see cref="MMDeviceEnumerator"/> (AGENTS.md stack rules). Windows-only thin adapter;
/// not covered by unit tests (CI has no Windows audio) — verified on a real machine.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = new List<AudioEndpoint>();
        AppendFlow(enumerator, DataFlow.Render, DeviceFlow.Render, endpoints);
        AppendFlow(enumerator, DataFlow.Capture, DeviceFlow.Capture, endpoints);
        return endpoints;
    }

    private static void AppendFlow(
        MMDeviceEnumerator enumerator,
        DataFlow dataFlow,
        DeviceFlow flow,
        List<AudioEndpoint> endpoints)
    {
        var defaultId = TryGetDefaultEndpointId(enumerator, dataFlow);
        foreach (var device in enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active))
        {
            using (device)
            {
                endpoints.Add(new AudioEndpoint(
                    device.ID,
                    device.FriendlyName,
                    flow,
                    string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    private static string? TryGetDefaultEndpointId(MMDeviceEnumerator enumerator, DataFlow dataFlow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
            return device.ID;
        }
        catch (COMException)
        {
            // No default endpoint for this flow (e.g. no microphone attached) — not an error.
            return null;
        }
    }
}
