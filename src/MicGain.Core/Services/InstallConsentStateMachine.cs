using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <summary>
/// Pure state machine for the install-consent flow (T2.1 / issue #6, MAIN PLAN T2.1).
/// Allowed transitions:
/// <code>
/// ApoNotInstalled → AwaitingConsent   (BeginConsent — default output device known)
/// ApoNotInstalled → NoDevice          (ReportNoOutputDevice)
/// AwaitingConsent → Declined          (Decline)
/// AwaitingConsent → Ready             (Accept — stub; T2.2 inserts Installing)
/// </code>
/// NoDevice, Declined and Ready are terminal. Invalid transitions throw
/// <see cref="InvalidOperationException"/> — the flow can never skip the consent step
/// (AGENTS.md rule 2: no system change without explicit consent).
/// Pure C#: no UI references, no registry/FS/audio access (AGENTS.md rules 3 and 6).
/// </summary>
public sealed class InstallConsentStateMachine
{
    public InstallFlowState State { get; private set; } = InstallFlowState.ApoNotInstalled;

    /// <summary>The device named in the consent dialog; set by <see cref="BeginConsent"/>.</summary>
    public AudioDeviceInfo? DefaultOutputDevice { get; private set; }

    public bool IsTerminal =>
        State is InstallFlowState.NoDevice or InstallFlowState.Declined or InstallFlowState.Ready;

    /// <summary>Starts the consent step for the given default output (render) device.</summary>
    public void BeginConsent(AudioDeviceInfo defaultOutputDevice)
    {
        ArgumentNullException.ThrowIfNull(defaultOutputDevice);
        if (defaultOutputDevice.Flow != DeviceFlow.Render)
        {
            throw new ArgumentException(
                "The consent dialog must name an output (render) device.", nameof(defaultOutputDevice));
        }

        Require(InstallFlowState.ApoNotInstalled, nameof(BeginConsent));
        DefaultOutputDevice = defaultOutputDevice;
        State = InstallFlowState.AwaitingConsent;
    }

    /// <summary>No active output device exists — the flow ends before any consent is asked.</summary>
    public void ReportNoOutputDevice()
    {
        Require(InstallFlowState.ApoNotInstalled, nameof(ReportNoOutputDevice));
        State = InstallFlowState.NoDevice;
    }

    /// <summary>User declined: terminal, and by definition zero system changes were made.</summary>
    public void Decline()
    {
        Require(InstallFlowState.AwaitingConsent, nameof(Decline));
        State = InstallFlowState.Declined;
    }

    /// <summary>
    /// User consented. Stub transition straight to <see cref="InstallFlowState.Ready"/> —
    /// T2.2 will insert the Installing state and the actual install logic here.
    /// </summary>
    public void Accept()
    {
        Require(InstallFlowState.AwaitingConsent, nameof(Accept));
        State = InstallFlowState.Ready;
    }

    private void Require(InstallFlowState expected, string action)
    {
        if (State != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {action} while in state '{State}'; expected '{expected}'.");
        }
    }
}