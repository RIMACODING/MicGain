namespace MicGain.Core.Models;

/// <summary>
/// Audio endpoint data-flow direction. Maps 1:1 to the <c>Render</c> / <c>Capture</c> registry
/// branches under <c>MMDevices\Audio</c> (dev-ref §Registry changes item 4).
/// </summary>
public enum DeviceFlow
{
    /// <summary>Output device (speakers, headphones).</summary>
    Render,

    /// <summary>Input device (microphone). Supports only one LFX APO — no GFX [DOC].</summary>
    Capture,
}
