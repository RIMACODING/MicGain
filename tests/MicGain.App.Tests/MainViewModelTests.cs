using System.IO;
using MicGain.App.ViewModels;
using MicGain.Core.Models;
using Xunit;

namespace MicGain.App.Tests;

public sealed class MainViewModelTests
{
    private const string MicGuid1 = "{b0000000-0000-0000-0000-000000000001}";
    private const string MicGuid2 = "{b0000000-0000-0000-0000-000000000002}";
    private const string SpeakerGuid = "{a0000000-0000-0000-0000-000000000001}";

    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    private readonly FakeApoDetectionService _detection = new();
    private readonly FakeAudioDeviceService _devices = new();
    private readonly FakeApoConfigService _config = new();
    private readonly List<string> _factoryCalls = new();

    /// <summary>Config path used in tests — matches Installed() fixture below.</summary>
    private const string TestConfigPath = @"C:\cfg";

    private MainViewModel CreateViewModel() =>
        new(TestConfigPath, _detection, _devices,
            configDirectory =>
            {
                _factoryCalls.Add(configDirectory);
                return _config;
            },
            TimeSpan.Zero, NoDelay);

    private void AddCapture(string guid, string name = "Mic", bool apoEnabled = true, bool isDefault = false) =>
        _devices.Devices.Add(new AudioDeviceInfo(name, guid, DeviceFlow.Capture, isDefault, apoEnabled));

    private void AddRender(string guid, string name = "Speakers") =>
        _devices.Devices.Add(new AudioDeviceInfo(name, guid, DeviceFlow.Render, true, true));

    [Fact]
    public async Task LoadAsync_ApoNotInstalled_SetsStateAndNeverCreatesConfigService()
    {
        _detection.Result = ApoDetectionResult.NotInstalled;

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Equal(AppState.ApoNotInstalled, vm.State);
        Assert.Empty(vm.Devices);
        Assert.Empty(_factoryCalls);
    }

    [Fact]
    public async Task LoadAsync_PassesDetectedConfigPathToConfigServiceFactory()
    {
        _detection.Result = ApoDetectionResult.Installed(@"D:\Apps\APO\config");
        AddCapture(MicGuid1);

        await CreateViewModel().LoadAsync();

        // Config path is passed from the constructor, not from re-detection.
        Assert.Equal(new[] { TestConfigPath }, _factoryCalls);
    }

    [Fact]
    public async Task LoadAsync_NoCaptureDevices_SetsNoCaptureDevicesState()
    {
        _detection.Result = ApoDetectionResult.Installed(TestConfigPath);
        AddRender(SpeakerGuid);

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Equal(AppState.NoCaptureDevices, vm.State);
        Assert.Empty(vm.Devices);
    }

    [Fact]
    public async Task LoadAsync_ListsOnlyCaptureDevices_WithNameAndGuid()
    {
        _detection.Result = ApoDetectionResult.Installed(TestConfigPath);
        AddRender(SpeakerGuid);
        AddCapture(MicGuid1, "USB Microphone", isDefault: true);
        AddCapture(MicGuid2, "Headset Mic", apoEnabled: false);

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Equal(AppState.Ready, vm.State);
        Assert.Equal(2, vm.Devices.Count);
        Assert.Equal("USB Microphone", vm.Devices[0].FriendlyName);
        Assert.Equal(MicGuid1, vm.Devices[0].EndpointGuid);
        Assert.True(vm.Devices[0].IsApoEnabled);
        Assert.False(vm.Devices[1].IsApoEnabled);
    }

    [Fact]
    public async Task LoadAsync_SliderValuesReflectStoredGains()
    {
        _detection.Result = ApoDetectionResult.Installed(TestConfigPath);
        _config.StoredGains[MicGuid1] = -12;
        AddCapture(MicGuid1);
        AddCapture(MicGuid2);

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Equal(-12, vm.Devices[0].GainDb);
        Assert.Equal(GainRange.DefaultDb, vm.Devices[1].GainDb);
    }

    [Fact]
    public async Task LoadAsync_StoredGainOutsideRange_IsClamped()
    {
        _detection.Result = ApoDetectionResult.Installed(TestConfigPath);
        _config.StoredGains[MicGuid1] = -100;
        AddCapture(MicGuid1);

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Equal(GainRange.MinDb, vm.Devices[0].GainDb);
    }

    [Fact]
    public async Task LoadAsync_NeverWritesConfig()
    {
        _detection.Result = ApoDetectionResult.Installed(TestConfigPath);
        _config.StoredGains[MicGuid1] = -6;
        AddCapture(MicGuid1);
        AddCapture(MicGuid2);

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Empty(_config.Writes);
    }

    [Fact]
    public async Task DeviceWriteFailure_SurfacesAsNonBlockingStatusMessage()
    {
        _detection.Result = ApoDetectionResult.Installed(TestConfigPath);
        AddCapture(MicGuid1, "USB Microphone");
        var vm = CreateViewModel();
        await vm.LoadAsync();
        _config.ThrowOnWrite = new IOException("disk full");

        vm.Devices[0].GainDb = -3;
        await vm.Devices[0].PendingWrite;

        Assert.Equal(AppState.Ready, vm.State);
        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("disk full", vm.StatusMessage);
        Assert.Contains("USB Microphone", vm.StatusMessage);
    }

    [Fact]
    public async Task LoadAsync_DetectionThrows_SetsErrorStateWithoutCrashing()
    {
        _detection.ThrowOnDetect = new InvalidOperationException("registry unavailable");

        var vm = CreateViewModel();
        await vm.LoadAsync();

        Assert.Equal(AppState.Error, vm.State);
        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("registry unavailable", vm.StatusMessage);
    }
}