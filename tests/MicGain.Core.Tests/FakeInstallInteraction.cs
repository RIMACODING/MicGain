using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.Core.Tests;

/// <summary>
/// Scriptable <see cref="IInstallInteraction"/>: consent answers per <see cref="SystemChange"/>
/// and an optional callback simulating what the user does in the Configurator window.
/// </summary>
public sealed class FakeInstallInteraction : IInstallInteraction
{
    /// <summary>Consent answer per change; all granted by default.</summary>
    public Dictionary<SystemChange, bool> Consents { get; } = new()
    {
        [SystemChange.RunInstaller] = true,
        [SystemChange.RunConfigurator] = true,
        [SystemChange.WriteRegistry] = true,
        [SystemChange.RestartAudioService] = true,
    };

    /// <summary>Every consent request the service made, in order — asserts the AC2 gating.</summary>
    public List<SystemChange> ConsentsRequested { get; } = new();

    public int ConfiguratorWaits { get; private set; }

    /// <summary>
    /// Runs when the guided Configurator step is reached — tests use it to simulate the user
    /// enabling (or not enabling) the device before the AC5 verification check.
    /// </summary>
    public Action<AudioDeviceInfo>? OnConfiguratorWait { get; set; }

    public Task<bool> ConfirmSystemChangeAsync(SystemChange change, string details)
    {
        ConsentsRequested.Add(change);
        return Task.FromResult(Consents[change]);
    }

    public Task WaitForConfiguratorAsync(AudioDeviceInfo device)
    {
        ConfiguratorWaits++;
        OnConfiguratorWait?.Invoke(device);
        return Task.CompletedTask;
    }
}