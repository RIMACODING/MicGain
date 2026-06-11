using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <summary>
/// Enumerates render and capture devices with friendly name, normalized endpoint GUID,
/// default-device flag, and per-device Equalizer APO enablement (issue #5, T1.1).
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>
    /// Active render and capture devices. Endpoints whose Core Audio ID cannot be
    /// normalized to a bare <c>{guid}</c> are skipped (fail safe — no guessed registry paths).
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetDevices();
}
