using MicGain.Core.IO;
using MicGain.Core.Models;
using MicGain.Core.Services;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class ApoInstallServiceTests
{
    private const string SpeakersGuid = "{a0000000-0000-0000-0000-000000000001}";
    private const string VendorClsid = "{11111111-1111-1111-1111-111111111111}";
    private const string InstallerPath = @"C:appassetsinstallerEqualizerAPO64-1.4.2.exe";

    private static readonly string FxKeyPath =
        $@"{AudioDeviceService.MmDevicesAudioKeyPath}Render{SpeakersGuid}FxProperties";

    private static readonly string BackupKeyPath =
        $@"{ApoInstallService.ChildAposKeyPath}{SpeakersGuid}";

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

    //