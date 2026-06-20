using MicGain.App.ViewModels;
using MicGain.Core.Models;
using Xunit;

namespace MicGain.App.Tests;

public sealed class InstallConsentViewModelTests
{
    private const string SpeakersGuid = "{a0000000-0000-0000-0000-000000000001}";
    private const string MonitorGuid = "{a0000000-0000-0000-0000-000000000002}";
    private const string MicGuid = "{b0000000-0000-0000-0000-000000000001}";

    private readonly FakeAudioDeviceService _devices = new();

    private void AddRender(string guid, string name, bool isDefault = false) =>
        _devices.Devices.Add(new AudioDeviceInfo(name, guid, DeviceFlow.Render, isDefault, false));

    private void AddCapture(string guid, string name) =>
        _devices.Devices.Add(new AudioDeviceInfo(name, guid, DeviceFlow.Capture, true, false));

    private InstallConsentViewModel CreateInitializedViewModel()
    {
        var viewModel = new InstallConsentViewModel(_devices);
        viewModel.Initialize();
        return viewModel;
    }

    [Fact]
    public void Initialize_NoRenderDevices_ReportsNoDevice()
    {
        // Capture-only machines count as "no active output device" (acceptance criterion 2).
        AddCapture(MicGuid, "Microphone");

        var viewModel = CreateInitializedViewModel();

        Assert.Equal(InstallFlowState.NoDevice, viewModel.State);
        Assert.True(viewModel.StateMachine.IsTerminal);
        Assert.Contains("No changes were made", viewModel.NoDeviceMessage);
    }

    [Fact]
    public void Initialize_NamesTheActualDefaultOutputDevice()
    {
        // Acceptance criterion 3: with multiple output devices, the consent dialog names
        // the default one — not the first enumerated.
        AddRender(MonitorGuid, "Monitor Audio");
        AddRender(SpeakersGuid, "Speakers (Realtek)", isDefault: true);
        AddCapture(MicGuid, "Microphone");

        var viewModel = CreateInitializedViewModel();

        Assert.Equal(InstallFlowState.AwaitingConsent, viewModel.State);
        Assert.Equal(SpeakersGuid, viewModel.DefaultOutputDevice?.EndpointGuid);
        Assert.NotNull(viewModel.ConsentMessage);
        Assert.Contains("Speakers (Realtek)", viewModel.ConsentMessage);
        Assert.DoesNotContain("Monitor Audio", viewModel.ConsentMessage);
    }

    [Fact]
    public void Initialize_NoDefaultFlag_FallsBackToFirstRenderDevice()
    {
        AddRender(MonitorGuid, "Monitor Audio");
        AddRender(SpeakersGuid, "Speakers (Realtek)");

        var viewModel = CreateInitializedViewModel();

        Assert.Equal(InstallFlowState.AwaitingConsent, viewModel.State);
        Assert.Equal(MonitorGuid, viewModel.DefaultOutputDevice?.EndpointGuid);
    }

    [Fact]
    public void AcceptCommand_TransitionsToInstalling_AndRequestsClose()
    {
        // T2.2 (issue #3): Accept transitions to Installing; the install flow then drives
        // CompleteInstall() → Ready or FailInstall() → InstallFailed.
        AddRender(SpeakersGuid, "Speakers", isDefault: true);
        var viewModel = CreateInitializedViewModel();
        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        viewModel.AcceptCommand.Execute(null);

        Assert.Equal(InstallFlowState.Installing, viewModel.State);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void DeclineCommand_TransitionsToDeclined_AndRequestsClose()
    {
        // Acceptance criterion 1: decline is terminal; no service is ever invoked that
        // could change the system (the ViewModel only reads the device list).
        AddRender(SpeakersGuid, "Speakers", isDefault: true);
        var viewModel = CreateInitializedViewModel();
        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        viewModel.DeclineCommand.Execute(null);

        Assert.Equal(InstallFlowState.Declined, viewModel.State);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void Commands_DisabledBeforeInitialize()
    {
        var viewModel = new InstallConsentViewModel(_devices);

        Assert.False(viewModel.AcceptCommand.CanExecute(null));
        Assert.False(viewModel.DeclineCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_EnabledOnlyWhileAwaitingConsent()
    {
        AddRender(SpeakersGuid, "Speakers", isDefault: true);
        var viewModel = CreateInitializedViewModel();

        Assert.True(viewModel.AcceptCommand.CanExecute(null));
        Assert.True(viewModel.DeclineCommand.CanExecute(null));

        viewModel.DeclineCommand.Execute(null);

        Assert.False(viewModel.AcceptCommand.CanExecute(null));
        Assert.False(viewModel.DeclineCommand.CanExecute(null));
    }

    [Fact]
    public void DialogClosedWithoutAnswering_CountsAsDecline()
    {
        AddRender(SpeakersGuid, "Speakers", isDefault: true);
        var viewModel = CreateInitializedViewModel();

        viewModel.HandleDialogClosed();

        Assert.Equal(InstallFlowState.Declined, viewModel.State);
    }

    [Fact]
    public void DialogClosedAfterAccept_KeepsInstallingState()
    {
        // Closing the dialog after Accept does not override the Installing state — the install
        // flow has already been handed off to App.xaml.cs.
        AddRender(SpeakersGuid, "Speakers", isDefault: true);
        var viewModel = CreateInitializedViewModel();
        viewModel.AcceptCommand.Execute(null);

        viewModel.HandleDialogClosed();

        Assert.Equal(InstallFlowState.Installing, viewModel.State);
    }

    [Fact]
    public void DialogClosedInNoDeviceState_StaysNoDevice()
    {
        var viewModel = CreateInitializedViewModel(); // no devices at all

        viewModel.HandleDialogClosed();

        Assert.Equal(InstallFlowState.NoDevice, viewModel.State);
    }
}