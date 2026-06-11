using MicGain.Core.Audio;
using MicGain.Core.IO;
using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <summary>
/// <inheritdoc cref="IAudioDeviceService"/>
/// APO-enablement detection conforms to <c>docs/internal/apo-development-reference.md</c>
/// §Registry changes item 4 (canonical) and <c>docs/research-notes.md</c> §5:
/// <list type="bullet">
/// <item><c>{d04e05a6-...},5</c>/<c>,6</c> (Windows 8.1+) take precedence — when present, the
/// corresponding legacy <c>,1</c>/<c>,2</c> value is ignored [DOC].</item>
/// <item>Capture devices support only one LFX APO — the GFX slot is never consulted [DOC].</item>
/// <item>A device is APO-enabled iff an Equalizer APO CLSID is present in at least one
/// effective LFX/GFX slot (covers Configurator's per-stage troubleshooting options).</item>
/// <item>Reading <c>FxProperties</c> requires no elevation [DOC].</item>
/// </list>
/// </summary>
public sealed class AudioDeviceService : IAudioDeviceService
{
    /// <summary>LFX/GFX APO property key in FxProperties [DOC] (dev-ref §Registry changes item 4).</summary>
    public const string FxApoPropertyKey = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";

    public const string MmDevicesAudioKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";

    /// <summary>APO COM registration points [DOC] (dev-ref §Registry changes items 2–3).</summary>
    public const string AudioProcessingObjectsKeyPath = @"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects";

    public const string ClsidKeyPath = @"SOFTWARE\Classes\CLSID";

    private readonly IAudioDeviceEnumerator _enumerator;
    private readonly IRegistryReader _registry;

    public AudioDeviceService(IAudioDeviceEnumerator enumerator, IRegistryReader registry)
    {
        _enumerator = enumerator;
        _registry = registry;
    }

    public IReadOnlyList<AudioDeviceInfo> GetDevices()
    {
        var apoClsids = ResolveEqualizerApoClsids();
        var devices = new List<AudioDeviceInfo>();

        foreach (var endpoint in _enumerator.EnumerateActiveEndpoints())
        {
            if (!CoreAudioEndpointId.TryExtractEndpointGuid(endpoint.Id, out var endpointGuid))
            {
                continue; // fail safe: never derive registry paths from malformed IDs
            }

            devices.Add(new AudioDeviceInfo(
                endpoint.FriendlyName,
                endpointGuid,
                endpoint.Flow,
                endpoint.IsDefaultDevice,
                IsApoEnabled(endpoint.Flow, endpointGuid, apoClsids)));
        }

        return devices;
    }

    /// <summary>
    /// Resolves Equalizer APO's COM CLSID(s) at runtime instead of hardcoding a GUID: the
    /// concrete CLSID value is not documented (research-notes §5 — NEEDS-VM-VERIFICATION).
    /// A CLSID registered under <c>AudioEngine\AudioProcessingObjects</c> is treated as
    /// Equalizer APO when its COM class name or its <c>InprocServer32</c> path contains
    /// "EqualizerAPO" [DOC] (dev-ref §Registry changes items 2–3: class name is the default
    /// value of the CLSID key, DLL path is the default value of InprocServer32).
    /// </summary>
    private IReadOnlySet<string> ResolveEqualizerApoClsids()
    {
        var clsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registered = _registry.GetLocalMachineSubKeyNames(AudioProcessingObjectsKeyPath);
        if (registered is null)
        {
            return clsids;
        }

        foreach (var clsid in registered)
        {
            var className = _registry.GetLocalMachineString($@"{ClsidKeyPath}\{clsid}", "");
            var serverPath = _registry.GetLocalMachineString($@"{ClsidKeyPath}\{clsid}\InprocServer32", "");
            if (ContainsEqualizerApo(className) || ContainsEqualizerApo(serverPath))
            {
                clsids.Add(clsid);
            }
        }

        return clsids;
    }

    private static bool ContainsEqualizerApo(string? value) =>
        value is not null && value.Contains("EqualizerAPO", StringComparison.OrdinalIgnoreCase);

    private bool IsApoEnabled(DeviceFlow flow, string endpointGuid, IReadOnlySet<string> apoClsids)
    {
        if (apoClsids.Count == 0)
        {
            return false; // Equalizer APO's COM class is not registered at all
        }

        var branch = flow == DeviceFlow.Render ? "Render" : "Capture";
        var fxKeyPath = $@"{MmDevicesAudioKeyPath}\{branch}\{endpointGuid}\FxProperties";

        // LFX slot: ,5 (Win 8.1+) takes precedence; when it exists, ,1 is ignored [DOC].
        if (SlotContainsApo(fxKeyPath, modernSuffix: 5, legacySuffix: 1, apoClsids))
        {
            return true;
        }

        // GFX slot exists on render devices only — capture supports a single LFX APO [DOC].
        return flow == DeviceFlow.Render
            && SlotContainsApo(fxKeyPath, modernSuffix: 6, legacySuffix: 2, apoClsids);
    }

    private bool SlotContainsApo(string fxKeyPath, int modernSuffix, int legacySuffix, IReadOnlySet<string> apoClsids)
    {
        // Null-coalescing implements the precedence rule: the legacy value is read only
        // when the modern value does not exist (dev-ref §Registry changes item 4).
        var value = _registry.GetLocalMachineString(fxKeyPath, $"{FxApoPropertyKey},{modernSuffix}")
                    ?? _registry.GetLocalMachineString(fxKeyPath, $"{FxApoPropertyKey},{legacySuffix}");
        if (value is null)
        {
            return false;
        }

        return apoClsids.Any(clsid => value.Contains(clsid, StringComparison.OrdinalIgnoreCase));
    }
}
