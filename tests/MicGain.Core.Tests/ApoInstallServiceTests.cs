using MicGain.Core.IO;
using MicGain.Core.Models;
using MicGain.Core.Services;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class ApoInstallServiceTests
{
    private const string SpeakersGuid = "{a0000000-0000-0000-0000-000000000001}";
    private const string VendorClsid = "{11111111-1111-1111-1111-111111111111}";
    private const string InstallerPath = @"C:\app\assets\installer\EqualizerAPO64-1.4.2.exe";

    private static readonly string FxKeyPath =
        $@"{AudioDeviceService.MmDevicesAudioKeyPath}\Render\{SpeakersGuid}\FxProperties";

    private static readonly string BackupKeyPath =
        $@"{ApoInstallService.ChildAposKeyPath}\{SpeakersGuid}";

    private static readonly string LfxValueName = $"{AudioDeviceService.FxApoPropertyKey},5";
    private static readonly string GfxValueName = $"{AudioDeviceService.FxApoPropertyKey},6";
    private static readonly string LegacyLfxValueName = $"{AudioDeviceService.FxApoPropertyKey},1";
    private static readonly string LegacyGfxValueName = $"{AudioDeviceService.FxApoPropertyKey},2";

    private readonly FakeFileSystem _fileSystem = new();
    private readonly FakeRegistryReader _registry = new();
    private readonly FakeRegistryWriter _registryWriter;
    private readonly FakeProcessRunner _processRunner = new();
    private readonly FakeInstallInteraction _interaction = new();

    public ApoInstallServiceTests()
    {
        _registryWriter = new FakeRegistryWriter(_registry);
        _fileSystem.Files[InstallerPath] = "<nsis installer bytes>";
    }

    private static AudioDeviceInfo Speakers() =>
        new("Speakers", SpeakersGuid, DeviceFlow.Render, true, false);

    private static AudioDeviceInfo Microphone() =>
        new("Microphone", "{b0000000-0000-0000-0000-000000000001}", DeviceFlow.Capture, true, false);

    private ApoInstallService CreateService(string installerPath = InstallerPath) =>
        new(_fileSystem, _registry, _registryWriter, _processRunner, _interaction, installerPath);

    private void SimulateUserEnablingDeviceInConfigurator() =>
        _interaction.OnConfiguratorWait = _ =>
            _registry.SetValue(FxKeyPath, LfxValueName, ApoInstallService.EqualizerApoLfxClsid);

    // --------------------------------------------------------------------------------------
    // Guided install (primary path)
    // --------------------------------------------------------------------------------------

    [Fact]
    public async Task GuidedInstall_HappyPath_Succeeds_WithConsentedInstallerAndRestart()
    {
        SimulateUserEnablingDeviceInConfigurator();
        var service = CreateService();

        var outcome = await service.RunGuidedInstallAsync(Speakers());

        Assert.Equal(InstallOutcome.Succeeded, outcome);
        Assert.Equal(3, _processRunner.Launched.Count);
        Assert.Equal((InstallerPath, "/S"), _processRunner.Launched[0]);
        // Two separate net.exe calls: stop then start (no && short-circuit).
        Assert.Equal(("net.exe", "stop audiosrv /y"), _processRunner.Launched[1]);
        Assert.Equal(("net.exe", "start audiosrv"), _processRunner.Launched[2]);
        Assert.Equal(1, _interaction.ConfiguratorWaits);
        // AC2: each system change was individually consented, in order.
        Assert.Equal(
            new[] { SystemChange.RunInstaller, SystemChange.RestartAudioService },
            _interaction.ConsentsRequested);
        Assert.True(service.IsDeviceEnabled(Speakers()));
    }

    [Fact]
    public async Task GuidedInstall_InstallerNotFound_NothingExecuted()
    {
        var service = CreateService(@"C:\does\not\exist.exe");

        var outcome = await service.RunGuidedInstallAsync(Speakers());

        Assert.Equal(InstallOutcome.InstallerNotFound, outcome);
        Assert.Empty(_processRunner.Launched);
        Assert.Empty(_interaction.ConsentsRequested);
    }

    [Fact]
    public async Task GuidedInstall_NoConsent_NothingExecuted()
    {
        _interaction.Consents[SystemChange.RunInstaller] = false;
        var service = CreateService();

        var outcome = await service.RunGuidedInstallAsync(Speakers());

        Assert.Equal(InstallOutcome.ConsentDeclined, outcome);
        Assert.Empty(_processRunner.Launched);
        Assert.Equal(0, _interaction.ConfiguratorWaits);
    }

    [Fact]
    public async Task GuidedInstall_UacPromptCancelled_CountsAsConsentDeclined()
    {
        _processRunner.NextExitCode = IProcessRunner.UacCancelledExitCode;
        var service = CreateService();

        var outcome = await service.RunGuidedInstallAsync(Speakers());

        Assert.Equal(InstallOutcome.ConsentDeclined, outcome);
        Assert.Equal(0, _interaction.ConfiguratorWaits); // flow stops before the Configurator step
    }

    [Fact]
    public async Task GuidedInstall_DeviceNotEnabledAfterConfigurator_ReportsWithoutRestart()
    {
        // The user closed the Configurator without ticking the device (issue #4 AC7).
        var service = CreateService();

        var outcome = await service.RunGuidedInstallAsync(Speakers());

        Assert.Equal(InstallOutcome.DeviceNotEnabled, outcome);
        Assert.Single(_processRunner.Launched); // installer only — no service restart
        Assert.DoesNotContain(SystemChange.RestartAudioService, _interaction.ConsentsRequested);
    }

    [Fact]
    public async Task GuidedInstall_RestartDeclined_SucceedsPendingRestart_NoSilentRestart()
    {
        SimulateUserEnablingDeviceInConfigurator();
        _interaction.Consents[SystemChange.RestartAudioService] = false;
        var service = CreateService();

        var outcome = await service.RunGuidedInstallAsync(Speakers());

        Assert.Equal(InstallOutcome.SucceededPendingRestart, outcome);
        Assert.Single(_processRunner.Launched); // AGENTS.md rule 2: never restart silently
    }

    // --------------------------------------------------------------------------------------
    // Registry enablement (advanced/optional path)
    // --------------------------------------------------------------------------------------

    [Fact]
    public async Task RegistryPath_HappyPath_WritesVerifiedSpec_AndBacksUpVendorValues()
    {
        // Device with a pre-existing vendor LFX APO in the legacy slot (research-notes §11).
        _registry.SetValue(FxKeyPath, LegacyLfxValueName, VendorClsid);
        var service = CreateService();

        var outcome = await service.EnableDeviceViaRegistryAsync(Speakers());

        Assert.Equal(InstallOutcome.Succeeded, outcome);

        // AC3: Child APOs backup mirrors Configurator's format so APO's uninstaller can restore.
        Assert.Equal(VendorClsid, _registry.GetLocalMachineString(BackupKeyPath, LegacyLfxValueName));
        Assert.Equal("2", _registry.GetLocalMachineString(BackupKeyPath, "Version"));

        // AC3: FxProperties per the VM-verified spec.
        Assert.Equal(ApoInstallService.EqualizerApoLfxClsid,
            _registry.GetLocalMachineString(FxKeyPath, LfxValueName));
        Assert.Equal(ApoInstallService.EqualizerApoGfxClsid,
            _registry.GetLocalMachineString(FxKeyPath, GfxValueName));
        Assert.Equal(ApoInstallService.EqualizerApoLfxClsid,
            _registry.GetLocalMachineString(FxKeyPath, LegacyLfxValueName)); // ,1 pre-existed → rewritten
        Assert.Null(_registry.GetLocalMachineString(FxKeyPath, LegacyGfxValueName)); // ,2 did not pre-exist
        Assert.Equal(ApoInstallService.DefaultProcessingMode,
            _registry.GetLocalMachineString(FxKeyPath, $"{ApoInstallService.ProcessingModePropertyKey},5"));
        Assert.Equal(ApoInstallService.DefaultProcessingMode,
            _registry.GetLocalMachineString(FxKeyPath, $"{ApoInstallService.ProcessingModePropertyKey},6"));
        Assert.Equal("1", _registry.GetLocalMachineString(
            ApoInstallService.AudioPolicyKeyPath, ApoInstallService.DisableProtectedAudioDgValueName));

        Assert.True(service.IsDeviceEnabled(Speakers()));
    }

    [Fact]
    public async Task RegistryPath_NoConsent_WritesNothing()
    {
        _interaction.Consents[SystemChange.WriteRegistry] = false;
        var service = CreateService();

        var outcome = await service.EnableDeviceViaRegistryAsync(Speakers());

        Assert.Equal(InstallOutcome.ConsentDeclined, outcome);
        Assert.Empty(_registry.Keys);
        Assert.Empty(_processRunner.Launched);
    }

    [Fact]
    public async Task RegistryPath_WriteFailsMidFlow_RollsBackEverything()
    {
        // AC4: a pre-existing vendor APO in the modern LFX slot must survive a failed enable.
        _registry.SetValue(FxKeyPath, LfxValueName, VendorClsid);
        // Write order: backup ,5 (1), backup Version (2), fx ,5 (3), fx ,6 (4),
        // processing-mode ,5 (5), processing-mode ,6 (6) → fails here.
        _registryWriter.ThrowOnWriteNumber = 6;
        var service = CreateService();

        var outcome = await service.EnableDeviceViaRegistryAsync(Speakers());

        Assert.Equal(InstallOutcome.FailedRolledBack, outcome);
        Assert.Equal(VendorClsid, _registry.GetLocalMachineString(FxKeyPath, LfxValueName)); // restored
        Assert.Null(_registry.GetLocalMachineString(FxKeyPath, GfxValueName));
        Assert.Null(_registry.GetLocalMachineString(FxKeyPath, $"{ApoInstallService.ProcessingModePropertyKey},5"));
        Assert.Null(_registry.GetLocalMachineString(
            ApoInstallService.AudioPolicyKeyPath, ApoInstallService.DisableProtectedAudioDgValueName));
        Assert.False(_registry.Keys.ContainsKey(BackupKeyPath)); // backup subkey removed (AC4)
        Assert.Empty(_processRunner.Launched); // no restart offered after a failed enable
        Assert.False(service.IsDeviceEnabled(Speakers()));
    }

    // --------------------------------------------------------------------------------------
    // Guards + detection
    // --------------------------------------------------------------------------------------

    [Fact]
    public async Task CaptureDevice_IsRejected_OnBothPaths()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunGuidedInstallAsync(Microphone()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.EnableDeviceViaRegistryAsync(Microphone()));
        Assert.Empty(_processRunner.Launched);
    }

    [Fact]
    public void IsDeviceEnabled_ChecksModernSlotFirst_ThenLegacy()
    {
        var service = CreateService();
        Assert.False(service.IsDeviceEnabled(Speakers()));

        // Legacy ,1 only → enabled (fallback).
        _registry.SetValue(FxKeyPath, LegacyLfxValueName, ApoInstallService.EqualizerApoLfxClsid);
        Assert.True(service.IsDeviceEnabled(Speakers()));

        // A modern ,5 value takes precedence and masks the legacy one [DOC] (dev-ref item 4).
        _registry.SetValue(FxKeyPath, LfxValueName, VendorClsid);
        Assert.False(service.IsDeviceEnabled(Speakers()));
    }
}