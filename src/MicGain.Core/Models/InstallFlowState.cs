namespace MicGain.Core.Models;

/// <summary>
/// States of the install-consent flow (T2.1 / issue #6). T2.2 will insert an
/// <c>Installing</c> state between <see cref="AwaitingConsent"/> and <see cref="Ready"/>
/// when the actual install logic lands.
/// </summary>
public enum InstallFlowState
{
    /// <summary>Equalizer APO is not installed; the flow has not started yet.</summary>
    ApoNotInstalled,

    /// <summary>Consent dialog is showing, naming the default output device.</summary>
    AwaitingConsent,

    /// <summary>No active output device — terminal; the app exits cleanly with a message.</summary>
    NoDevice,

    /// <summary>User declined — terminal; the app exits cleanly with zero system changes.</summary>
    Declined,

    /// <summary>User consented — terminal stub in T2.1 (no install logic yet, see T2.2).</summary>
    Ready,
}