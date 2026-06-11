using MicGain.Core.Models;
using MicGain.Core.Services;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class AudioDeviceServiceTests
{
    // Placeholder GUIDs — Equalizer APO's concrete CLSID is NEEDS-VM-VERIFICATION, which is
    // exactly why the service resolves it from COM registration at runtime instead of hardcoding.
    private const string ApoClsid = "{aaaaaaaa-bbbb-cccc-dddd-eeeeffff0000}";
    private const string VendorClsid = "{11111111-2222-3333-4444-555566667777}";

    private const string RenderGuid1 = "{a0000000-0000-0000-0000-000000000001}";
    private const string RenderGuid2 = "{a0000000-0000-0000-0000-000000000002}";
    private const string CaptureGuid1 = "{b0000000-0000-0000-0000-000000000001}";
    private const string CaptureGuid2 = "{b0000000-0000-0000-0000-000000000002}";

    private const string FxApoKey = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";

    private readonly FakeRegistryReader _registry = new();
    private readonly FakeAudioDeviceEnumerator _enumerator = new();

    private AudioDeviceService CreateService() => new(_enumerator, _registry);

    private void RegisterEqualizerApoComClass()
    {
        _registry.SetValue($@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\{ApoClsid}", "", "");
        _registry.SetValue($@"SOFTWARE\Classes\CLSID\{ApoClsid}", "", "EqualizerAPO Class");
        _registry.SetValue(
            $@"SOFTWARE\Classes\CLSID\{ApoClsid}\InprocServer32",
            "",
            @"C:\Program Files\EqualizerAPO\EqualizerAPO.dll");
    }

    private void AddRenderDevice(string endpointGuid, string name, bool isDefault = false) =>
        _enumerator.Endpoints.Add(new AudioEndpoint(
            $"{{0.0.0.00000000}}.{endpointGuid}", name, DeviceFlow.Render, isDefault));

    private void AddCaptureDevice(string endpointGuid, string name, bool isDefault = false) =>
        _enumerator.Endpoints.Add(new AudioEndpoint(
            $"{{0.0.1.00000000}}.{endpointGuid}", name, DeviceFlow.Capture, isDefault));

    private void SetFxValue(string branch, string endpointGuid, int suffix, string clsid) =>
        _registry.SetValue(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{branch}\{endpointGuid}\FxProperties",
            $"{FxApoKey},{suffix}",
            clsid);

    [Fact]
    public void GetDevices_MixedDevices_FlagsExactlyTheApoEnabledOnes()
    {
        // Mirrors acceptance criterion 1 with mocks: APO enabled on exactly 2 of 4 devices.
        RegisterEqualizerApoComClass();
        AddRenderDevice(RenderGuid1, "Speakers", isDefault: true);
        AddRenderDevice(RenderGuid2, "Monitor");
        AddCaptureDevice(CaptureGuid1, "Microphone", isDefault: true);
        AddCaptureDevice(CaptureGuid2, "Line In");
        SetFxValue("Render", RenderGuid1, 5, ApoClsid);   // modern LFX
        SetFxValue("Capture", CaptureGuid1, 5, ApoClsid); // modern LFX
        SetFxValue("Render", RenderGuid2, 5, VendorClsid); // vendor APO only — not ours

        var devices = CreateService().GetDevices();

        Assert.Equal(4, devices.Count);
        var enabled = devices.Where(d => d.IsApoEnabled).Select(d => d.EndpointGuid).ToList();
        Assert.Equal(2, enabled.Count);
        Assert.Contains(RenderGuid1, enabled);
        Assert.Contains(CaptureGuid1, enabled);
    }

    [Fact]
    public void GetDevices_PopulatesNameGuidFlowAndDefaultFlag()
    {
        RegisterEqualizerApoComClass();
        AddCaptureDevice(CaptureGuid1, "USB Microphone", isDefault: true);

        var device = Assert.Single(CreateService().GetDevices());

        Assert.Equal("USB Microphone", device.FriendlyName);
        Assert.Equal(CaptureGuid1, device.EndpointGuid); // bare normalized {guid}
        Assert.Equal(DeviceFlow.Capture, device.Flow);
        Assert.True(device.IsDefaultDevice);
        Assert.False(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_ModernSlotPresent_StaleLegacyApoEntryIsIgnored()
    {
        // Precedence rule [DOC]: when ,5 exists, ,1 is ignored — a stale APO CLSID in ,1
        // must not produce a false positive.
        RegisterEqualizerApoComClass();
        AddRenderDevice(RenderGuid1, "Speakers");
        SetFxValue("Render", RenderGuid1, 5, VendorClsid); // effective LFX = vendor
        SetFxValue("Render", RenderGuid1, 1, ApoClsid);    // stale legacy entry

        var device = Assert.Single(CreateService().GetDevices());

        Assert.False(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_ModernSlotWithApo_LegacyVendorEntryDoesNotMask()
    {
        RegisterEqualizerApoComClass();
        AddRenderDevice(RenderGuid1, "Speakers");
        SetFxValue("Render", RenderGuid1, 5, ApoClsid);
        SetFxValue("Render", RenderGuid1, 1, VendorClsid);

        var device = Assert.Single(CreateService().GetDevices());

        Assert.True(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_NoModernSlot_FallsBackToLegacyValue()
    {
        RegisterEqualizerApoComClass();
        AddRenderDevice(RenderGuid1, "Speakers");
        SetFxValue("Render", RenderGuid1, 1, ApoClsid); // legacy-only registration

        var device = Assert.Single(CreateService().GetDevices());

        Assert.True(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_RenderDevice_GfxSlotAloneCountsAsEnabled()
    {
        // "Enabled in at least one stage" rule (install-ref troubleshooting options).
        RegisterEqualizerApoComClass();
        AddRenderDevice(RenderGuid1, "Speakers");
        SetFxValue("Render", RenderGuid1, 6, ApoClsid); // GFX only

        var device = Assert.Single(CreateService().GetDevices());

        Assert.True(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_CaptureDevice_GfxSlotsAreNeverConsulted()
    {
        // Capture devices support only one LFX APO — no GFX [DOC] (dev-ref §APO development).
        RegisterEqualizerApoComClass();
        AddCaptureDevice(CaptureGuid1, "Microphone");
        SetFxValue("Capture", CaptureGuid1, 6, ApoClsid); // would mean "enabled" on render
        SetFxValue("Capture", CaptureGuid1, 2, ApoClsid);

        var device = Assert.Single(CreateService().GetDevices());

        Assert.False(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_ApoComClassNotRegistered_NoDeviceIsFlagged()
    {
        // Clean machine: even APO-looking FxProperties cannot match without a registered CLSID.
        AddRenderDevice(RenderGuid1, "Speakers");
        SetFxValue("Render", RenderGuid1, 5, ApoClsid);

        var device = Assert.Single(CreateService().GetDevices());

        Assert.False(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_ClsidResolvedViaInprocServer32PathOnly()
    {
        // Class name is vendor-ish, but InprocServer32 points at EqualizerAPO.dll.
        _registry.SetValue($@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\{ApoClsid}", "", "");
        _registry.SetValue($@"SOFTWARE\Classes\CLSID\{ApoClsid}", "", "Audio Effects Class");
        _registry.SetValue(
            $@"SOFTWARE\Classes\CLSID\{ApoClsid}\InprocServer32", "", @"D:\Apps\APO\EqualizerAPO.dll");
        AddCaptureDevice(CaptureGuid1, "Microphone");
        SetFxValue("Capture", CaptureGuid1, 5, ApoClsid);

        var device = Assert.Single(CreateService().GetDevices());

        Assert.True(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_ClsidComparisonIsCaseInsensitive()
    {
        RegisterEqualizerApoComClass();
        AddCaptureDevice(CaptureGuid1, "Microphone");
        SetFxValue("Capture", CaptureGuid1, 5, ApoClsid.ToUpperInvariant());

        var device = Assert.Single(CreateService().GetDevices());

        Assert.True(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_UppercaseCoreAudioId_NormalizedGuidStillMatchesRegistry()
    {
        RegisterEqualizerApoComClass();
        _enumerator.Endpoints.Add(new AudioEndpoint(
            $"{{0.0.1.00000000}}.{CaptureGuid1.ToUpperInvariant()}", "Microphone", DeviceFlow.Capture, false));
        SetFxValue("Capture", CaptureGuid1, 5, ApoClsid);

        var device = Assert.Single(CreateService().GetDevices());

        Assert.Equal(CaptureGuid1, device.EndpointGuid);
        Assert.True(device.IsApoEnabled);
    }

    [Fact]
    public void GetDevices_MalformedCoreAudioId_DeviceIsSkippedFailSafe()
    {
        RegisterEqualizerApoComClass();
        _enumerator.Endpoints.Add(new AudioEndpoint("bogus-id-without-guid", "Ghost", DeviceFlow.Render, false));
        AddRenderDevice(RenderGuid1, "Speakers");

        var devices = CreateService().GetDevices();

        var device = Assert.Single(devices);
        Assert.Equal("Speakers", device.FriendlyName);
    }

    [Fact]
    public void GetDevices_NoEndpoints_ReturnsEmptyList()
    {
        RegisterEqualizerApoComClass();

        Assert.Empty(CreateService().GetDevices());
    }
}
