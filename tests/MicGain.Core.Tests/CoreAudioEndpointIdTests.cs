using MicGain.Core.Audio;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class CoreAudioEndpointIdTests
{
    [Theory]
    // Render form {0.0.0.00000000}.{guid}
    [InlineData("{0.0.0.00000000}.{a1b2c3d4-1111-2222-3333-444455556666}", "{a1b2c3d4-1111-2222-3333-444455556666}")]
    // Capture form {0.0.1.00000000}.{guid}
    [InlineData("{0.0.1.00000000}.{a1b2c3d4-1111-2222-3333-444455556666}", "{a1b2c3d4-1111-2222-3333-444455556666}")]
    // Uppercase input is normalized to lowercase braced form
    [InlineData("{0.0.0.00000000}.{A1B2C3D4-1111-2222-3333-444455556666}", "{a1b2c3d4-1111-2222-3333-444455556666}")]
    public void TryExtractEndpointGuid_ValidCoreAudioId_ExtractsNormalizedTrailingGuid(string id, string expected)
    {
        var ok = CoreAudioEndpointId.TryExtractEndpointGuid(id, out var guid);

        Assert.True(ok);
        Assert.Equal(expected, guid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no guid here")]
    [InlineData("{0.0.0.00000000}")] // prefix only, no endpoint guid
    [InlineData("{0.0.0.00000000}.")]
    [InlineData("{0.0.0.00000000}.{not-a-guid}")]
    [InlineData("{0.0.0.00000000}.{a1b2c3d4-1111-2222-3333-444455556666}trailing")]
    public void TryExtractEndpointGuid_InvalidId_ReturnsFalse(string? id)
    {
        var ok = CoreAudioEndpointId.TryExtractEndpointGuid(id, out var guid);

        Assert.False(ok);
        Assert.Equal(string.Empty, guid);
    }
}
