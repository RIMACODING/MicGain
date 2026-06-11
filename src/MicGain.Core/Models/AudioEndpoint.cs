namespace MicGain.Core.Models;

/// <summary>
/// Raw endpoint as returned by <see cref="Audio.IAudioDeviceEnumerator"/>, before GUID
/// normalization and APO-enablement detection.
/// </summary>
/// <param name="Id">
/// Full Core Audio device ID, e.g. <c>{0.0.0.00000000}.{guid}</c> (render) or
/// <c>{0.0.1.00000000}.{guid}</c> (capture) — see <c>docs/research-notes.md</c> §5.
/// </param>
/// <param name="FriendlyName">Human-readable device name shown in the UI.</param>
/// <param name="Flow">Render or capture.</param>
/// <param name="IsDefaultDevice">True when this is the default multimedia endpoint for its flow.</param>
public sealed record AudioEndpoint(string Id, string FriendlyName, DeviceFlow Flow, bool IsDefaultDevice);
