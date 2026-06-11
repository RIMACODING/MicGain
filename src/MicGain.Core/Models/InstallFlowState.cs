namespace MicGain.Core.Models;

/// <summary>
/// States of the install flow (T2.1 consent — issue #3; T2.2 install — issue #4).
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

    /// <summary>User consented; the T2.2 install flow (issue #4) is running.</summary>
    Installing,

    /// <summary>Install failed or was abandoned (changes rolled back) — terminal.</summary>
    InstallFailed,

    /// <summary>Install completed — terminal; the app can proceed to the main window.</summary>
    Ready,
}
