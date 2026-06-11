using System.IO;
using MicGain.App.ViewModels;
using MicGain.Core.Models;
using Xunit;

namespace MicGain.App.Tests;

public sealed class DeviceGainViewModelTests
{
    private const string MicGuid = "{b0000000-0000-0000-0000-000000000001}";

    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    private readonly FakeApoConfigService _config = new();

    private static AudioDeviceInfo Mic(bool apoEnabled = true) =>
        new("USB Microphone", MicGuid, DeviceFlow.Capture, IsDefaultDevice: true, IsApoEnabled: apoEnabled);

    private DeviceGainViewModel CreateViewModel(
        double initialGain = 0,
        Action<string>? onError = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(Mic(), _config, new SemaphoreSlim(1, 1), onError ?? (_ => { }), initialGain,
            TimeSpan.FromMilliseconds(150), delay ?? NoDelay);

    [Fact]
    public async Task SettingGain_WritesExactPayloadForCorrectDevice()
    {
        var vm = CreateViewModel();

        vm.GainDb = -12.5;
        await vm.PendingWrite;

        var write = Assert.Single(_config.Writes);
        Assert.Equal(MicGuid, write.EndpointGuid);
        Assert.Equal(-12.5, write.GainDb);
    }

    [Fact]
    public async Task InitialGainFromStoredConfig_NeverTriggersWrite()
    {
        // Issue #4: the app never writes config without user slider interaction.
        var vm = CreateViewModel(initialGain: -6);

        await vm.PendingWrite;

        Assert.Equal(-6, vm.GainDb);
        Assert.Empty(_config.Writes);
    }

    [Theory]
    [InlineData(100, GainRange.MaxDb)]
    [InlineData(-100, GainRange.MinDb)]
    public async Task SettingGainOutsideRange_ClampsToCoreGainRange(double input, double expected)
    {
        var vm = CreateViewModel();

        vm.GainDb = input;
        await vm.PendingWrite;

        Assert.Equal(expected, vm.GainDb);
        var write = Assert.Single(_config.Writes);
        Assert.Equal(expected, write.GainDb);
    }

    [Fact]
    public async Task SettingSameValueTwice_WritesOnlyOnce()
    {
        var vm = CreateViewModel();

        vm.GainDb = -6;
        await vm.PendingWrite;
        vm.GainDb = -6;
        await vm.PendingWrite;

        Assert.Single(_config.Writes);
    }

    [Fact]
    public async Task RapidChanges_DebounceWritesOnlyTheFinalValue()
    {
        var delay = new ControllableDelay();
        var vm = CreateViewModel(delay: delay.DelayAsync);

        vm.GainDb = -3;
        vm.GainDb = -6;
        vm.GainDb = -9;
        delay.ReleaseAll();
        await vm.PendingWrite;

        var write = Assert.Single(_config.Writes);
        Assert.Equal(-9.0, write.GainDb);
    }

    [Fact]
    public async Task SequentialChanges_AreWrittenInOrder()
    {
        var vm = CreateViewModel();

        vm.GainDb = -3;
        await vm.PendingWrite;
        vm.GainDb = -6;
        await vm.PendingWrite;

        Assert.Equal(2, _config.Writes.Count);
        Assert.Equal(-3.0, _config.Writes[0].GainDb);
        Assert.Equal(-6.0, _config.Writes[1].GainDb);
    }

    [Fact]
    public async Task WriteFailure_ReportsNonBlockingErrorAndRecovers()
    {
        string? error = null;
        var vm = CreateViewModel(onError: message => error = message);
        _config.ThrowOnWrite = new IOException("disk full");

        vm.GainDb = -3;
        await vm.PendingWrite; // must complete without throwing (issue #4: no crash)

        Assert.NotNull(error);
        Assert.Contains("disk full", error);
        Assert.Contains("USB Microphone", error);

        // The ViewModel stays usable after the failure.
        _config.ThrowOnWrite = null;
        vm.GainDb = -6;
        await vm.PendingWrite;

        var write = Assert.Single(_config.Writes);
        Assert.Equal(-6.0, write.GainDb);
    }
}
