using MicGain.Core.Models;
using MicGain.Core.Services;

namespace MicGain.App.Tests;

public sealed class FakeApoDetectionService : IApoDetectionService
{
    public ApoDetectionResult Result { get; set; } = ApoDetectionResult.NotInstalled;

    public Exception? ThrowOnDetect { get; set; }

    public ApoDetectionResult Detect() => ThrowOnDetect is null ? Result : throw ThrowOnDetect;
}
