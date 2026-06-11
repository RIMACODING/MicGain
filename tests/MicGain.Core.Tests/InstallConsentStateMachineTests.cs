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
    public void Accept_FromAwaitingConsent_TransitionsToTerminalReadyStub()
    {
        var machine = new InstallConsentStateMachine();
        machine.BeginConsent(Speakers());

        machine.Accept();

        Assert.Equal(InstallFlowState.Ready, machine.State);
        Assert.True(machine.IsTerminal);
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

    public static TheoryData<string> TerminalSetups => new() { "declined", "accepted", "no-device" };

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
            case "accepted":
                machine.BeginConsent(Speakers());
                machine.Accept();
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
        Assert.Equal(terminalState, machine.State);
    }
}