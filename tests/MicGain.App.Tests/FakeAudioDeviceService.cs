using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App.Tests;

public sealed class FakeAudioDeviceService : IAudioDeviceService
{
    public List<AudioDeviceInfo> Devices { get; } = new();

    public IReadOnlyList<AudioDeviceInfo> GetDevices() => Devices;
}
