namespace MicGain.Core.Models;

/// <summary>
/// Audio device model returned by <see cref="Services.IAudioDeviceService"/> (issue #5, T1.1).
/// </summary>
/// <param name="FriendlyName">Human-readable device name.</param>
/// <param name="EndpointGuid">
/// Bare normalized <c>{guid}</c> extracted from the Core Audio ID — used as registry key under
/// <c>MMDevices\Audio\{Render|Capture}</c> and as Equalizer APO <c>Device:</c> selector.
/// </param>
/// <param name="Flow">Render or capture.</param>
/// <param name="IsDefaultDevice">True when this is the default multimedia endpoint for its flow.</param>
/// <param name="IsApoEnabled">
/// True when Equalizer APO is registered in at least one LFX/GFX slot of the device's
/// <c>FxProperties</c> key (dev-ref §Registry changes item 4).
/// </param>
public sealed record AudioDeviceInfo(
    string FriendlyName,
    string EndpointGuid,
    DeviceFlow Flow,
    bool IsDefaultDevice,
    bool IsApoEnabled);
