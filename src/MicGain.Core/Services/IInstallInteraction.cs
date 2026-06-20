using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <summary>
/// Consent gate + guidance callbacks for the T2.2 install flow (issue #4 AC2; AGENTS.md
/// rule 2: every system change is preceded by explicit user consent — the service never
/// mutates the system unless <see cref="ConfirmSystemChangeAsync"/> returned <c>true</c>
/// for that specific change). Implemented by the UI layer; mocked in tests (rule 3).
/// </summary>
public interface IInstallInteraction
{
    /// <summary>Asks the user to approve one specific system change. <c>false</c> = do not perform it.</summary>
    Task<bool> ConfirmSystemChangeAsync(SystemChange change, string details);

    /// <summary>
    /// Guided Configurator step (primary path — research-notes §11: <c>/S</c> does NOT
    /// suppress the Configurator device selector). Shows on-screen instructions naming the
    /// device and completes when the user confirms they selected it and closed Configurator.
    /// </summary>
    Task WaitForConfiguratorAsync(AudioDeviceInfo device);
}

/// <summary>A distinct, individually consented system mutation (issue #4 AC2).</summary>
public enum SystemChange
{
    /// <summary>Execute the bundled Equalizer APO installer (elevated).</summary>
    RunInstaller,

    /// <summary>Launch Equalizer APO Configurator (elevated) to enable a device without reinstalling.</summary>
    RunConfigurator,

    /// <summary>HKLM registry writes: FxProperties enablement, Child APOs backup, DisableProtectedAudioDG.</summary>
    WriteRegistry,

    /// <summary>Stop and start the Windows audio service (audiosrv).</summary>
    RestartAudioService,
}
