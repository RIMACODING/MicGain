namespace MicGain.Core.Models;

/// <summary>Result of an <see cref="Services.IApoInstallService"/> operation (T2.2, issue #4).</summary>
public enum InstallOutcome
{
    /// <summary>APO installed and enabled on the device; the audio service was restarted with consent.</summary>
    Succeeded,

    /// <summary>
    /// APO installed and enabled; the user declined the audio-service restart, so APO becomes
    /// active after the next restart/reboot. Whether a service restart alone (no reboot)
    /// reliably suffices is NEEDS-VM-VERIFICATION (research-notes §4/§11).
    /// </summary>
    SucceededPendingRestart,

    /// <summary>
    /// The user withheld consent for a system change (including cancelling the UAC prompt).
    /// No change beyond those already individually consented was made (AGENTS.md rule 2).
    /// </summary>
    ConsentDeclined,

    /// <summary>The bundled installer was not found on disk — nothing was executed (fail safe).</summary>
    InstallerNotFound,

    /// <summary>
    /// The Configurator was closed but the device's FxProperties LFX slot does not hold the
    /// Equalizer APO CLSID (issue #4 AC5) — the flow must not transition to Ready.
    /// </summary>
    DeviceNotEnabled,

    /// <summary>
    /// A registry write failed mid-flow; previously written values were restored from the
    /// journal and the Child APOs backup subkey was removed (issue #4 AC4, docs/rollback.md).
    /// </summary>
    FailedRolledBack,
}
