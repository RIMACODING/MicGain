using MicGain.Core.Models;
using MicGain.Core.Services;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class InstallConsentStateMachineTests
{
    private static AudioDeviceInfo Speakers() =>
        new("Speakers", "{a0000000-0000-0000-0000-000000000001}", DeviceFlow.Render, true, false);

    private static AudioDeviceInfo Microphone() =>
        new("Microphone", "{b0000000-0000-0000-0000-000000000001}", DeviceFlow.Capture, true, false);

    [Fact]
    public void InitialState_IsApoNotInstalled_AndNotTerminal()
    {
        var machine = new InstallConsentStateMachine();

        Assert.Equal(InstallFlowState.ApoNotInstalled, machine.State);
        Assert.False(machine.IsTerminal);
        Assert.Null(machine.DefaultOutputDevice);
    }

    [Fact]
    public void BeginConsent_TransitionsToAwaitingConsent_AndStoresDevice()
    {
        var machine = new InstallConsentStateMachine();
        var speakers = Speakers();

        machine.BeginConsent(speakers);

        Assert.Equal(InstallFlowState.AwaitingConsent, machine.State);
        Assert.Same(speakers, machine.DefaultOutputDevice);
        Assert.False(machine.IsTerminal);
    }

    [Fact]
    public void BeginConsent_WithCaptureDevice_ThrowsAndStaysPut()
    {
        var machine = new InstallConsentStateMachine();

        Assert.Throws<ArgumentException>(() => machine.BeginConsent(Microphone()));

        Assert.Equal(InstallFlowState.ApoNotInstalled, machine.State);
        Assert.Null(machine.DefaultOutputDevice);
    }

    [Fact]
    public void BeginConsent_Null_Throws()
    {
        var machine = new InstallConsentStateMachine();

        Assert.Throws<ArgumentNullException>(() => machine.BeginConsent(null!));
    }

    [Fact]
    public void ReportNoOutputDevice_TransitionsToTerminalNoDevice()
    {
        var machine = new InstallConsentStateMachine();

        machine.ReportNoOutputDevice();

        Assert.Equal(InstallFlowState.NoDevice, machine.State);
        Assert.True(machine.IsTerminal);
    }

    [Fact]
    public void Decline_FromAwaitingConsent_TransitionsToTerminalDeclined()
    {
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());

        machine.Decline();

        Assert.Equal(InstallFlowState.Declined, machine.State);
        Assert.True(machine.IsTerminal);
    }

    [Fact]
    public void Accept_FromAwaitingConsent_TransitionsToInstalling_NotTerminal()
    {
        // T2.2 (issue #4): Accept no longer jumps straight to Ready — the install flow runs.
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());

        machine.Accept();

        Assert.Equal(InstallFlowState.Installing, machine.State);
        Assert.False(machine.IsTerminal);
    }

    [Fact]
    public void CompleteInstall_FromInstalling_TransitionsToTerminalReady()
    {
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());
        machine.Accept();

        machine.CompleteInstall();

        Assert.Equal(InstallFlowState.Ready, machine.State);
        Assert.True(machine.IsTerminal);
    }

    [Fact]
    public void FailInstall_FromInstalling_TransitionsToTerminalInstallFailed()
    {
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());
        machine.Accept();

        machine.FailInstall();

        Assert.Equal(InstallFlowState.InstallFailed, machine.State);
        Assert.True(machine.IsTerminal);
    }

    [Fact]
    public void CompleteOrFailInstall_OutsideInstalling_Throws()
    {
        var machine = new InstallConsentStateMachine();

        Assert.Throws<InvalidOperationException>(machine.CompleteInstall);
        Assert.Throws<InvalidOperationException>(machine.FailInstall);

        machine.BeginConsent(Speakers());

        Assert.Throws<InvalidOperationException>(machine.CompleteInstall);
        Assert.Throws<InvalidOperationException>(machine.FailInstall);
        Assert.Equal(InstallFlowState.AwaitingConsent, machine.State);
    }

    [Fact]
    public void AcceptOrDecline_BeforeConsentRequested_Throws()
    {
        // Consent can never be skipped (AGENTS.md rule 2).
        var machine = new InstallConsentStateMachine();

        Assert.Throws<InvalidOperationException>(machine.Accept);
        Assert.Throws<InvalidOperationException>(machine.Decline);
        Assert.Equal(InstallFlowState.ApoNotInstalled, machine.State);
    }

    [Fact]
    public void BeginConsent_Twice_Throws()
    {
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());

        Assert.Throws<InvalidOperationException>(() => machine.BeginConsent(Speakers()));
        Assert.Equal(InstallFlowState.AwaitingConsent, machine.State);
    }

    [Fact]
    public void ReportNoOutputDevice_AfterConsentStarted_Throws()
    {
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());

        Assert.Throws<InvalidOperationException>(machine.ReportNoOutputDevice);
        Assert.Equal(InstallFlowState.AwaitingConsent, machine.State);
    }

    public static TheoryData<string> TerminalSetups =>
        new() { "declined", "install-completed", "install-failed", "no-device" };

    [Theory]
    [MemberData(nameof(TerminalSetups))]
    public void TerminalStates_RejectAllFurtherTransitions(string setup)
    {
        var machine = new InstallConsentStateMachine();
        switch (setup)
        {
            case "declined":
                machine.BeginConsent(Speakers());
                machine.Decline();
                break;
            case "install-completed":
                machine.BeginConsent(Speakers());
                machine.Accept();
                machine.CompleteInstall();
                break;
            case "install-failed":
                machine.BeginConsent(Speakers());
                machine.Accept();
                machine.FailInstall();
                break;
            case "no-device":
                machine.ReportNoOutputDevice();
                break;
        }

        var terminalState = machine.State;

        Assert.True(machine.IsTerminal);
        Assert.Throws<InvalidOperationException>(() => machine.BeginConsent(Speakers()));
        Assert.Throws<InvalidOperationException>(machine.ReportNoOutputDevice);
        Assert.Throws<InvalidOperationException>(machine.Accept);
        Assert.Throws<InvalidOperationException>(machine.Decline);
        Assert.Throws<InvalidOperationException>(machine.CompleteInstall);
        Assert.Throws<InvalidOperationException>(machine.FailInstall);
        Assert.Equal(terminalState, machine.State);
    }
}