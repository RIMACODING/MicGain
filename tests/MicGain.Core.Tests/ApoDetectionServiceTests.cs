using MicGain.Core.Services;
using Xunit;

namespace MicGain.Core.Tests;

public sealed class ApoDetectionServiceTests
{
    private const string ConfigDir = @"C:\Program Files\EqualizerAPO\config";

    private readonly FakeRegistryReader _registry = new();
    private readonly FakeFileSystem _fileSystem = new();

    private ApoDetectionService CreateService() => new(_registry, _fileSystem);

    [Fact]
    public void Detect_ConfigPathPresentAndDirectoryExists_ReturnsInstalledWithConfigPath()
    {
        _registry.SetValue(@"SOFTWARE\EqualizerAPO", "ConfigPath", ConfigDir);
        _fileSystem.Directories.Add(ConfigDir);

        var result = CreateService().Detect();

        Assert.True(result.IsInstalled);
        Assert.Equal(ConfigDir, result.ConfigPath);
    }

    [Fact]
    public void Detect_NonDefaultInstallPath_ResolvesFromRegistryNotHardcodedProgramFiles()
    {
        // install-ref explicitly supports custom install paths — never hardcode Program Files.
        _registry.SetValue(@"SOFTWARE\EqualizerAPO", "ConfigPath", @"D:\Apps\APO\config");
        _fileSystem.Directories.Add(@"D:\Apps\APO\config");

        var result = CreateService().Detect();

        Assert.True(result.IsInstalled);
        Assert.Equal(@"D:\Apps\APO\config", result.ConfigPath);
    }

    [Fact]
    public void Detect_RegistryValueAbsent_ReturnsNotInstalled()
    {
        // Clean machine: no HKLM\SOFTWARE\EqualizerAPO at all (acceptance criterion 2).
        var result = CreateService().Detect();

        Assert.False(result.IsInstalled);
        Assert.Null(result.ConfigPath);
    }

    [Fact]
    public void Detect_KeyPresentWithoutConfigPathValue_ReturnsNotInstalled()
    {
        _registry.SetValue(@"SOFTWARE\EqualizerAPO", "EnableTrace", "false");

        var result = CreateService().Detect();

        Assert.False(result.IsInstalled);
    }

    [Fact]
    public void Detect_ConfigPathPresentButDirectoryMissing_ReturnsNotInstalledFailSafe()
    {
        // Stale registry (acceptance criterion 4): value present, directory gone.
        _registry.SetValue(@"SOFTWARE\EqualizerAPO", "ConfigPath", ConfigDir);

        var result = CreateService().Detect();

        Assert.False(result.IsInstalled);
        Assert.Null(result.ConfigPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_ConfigPathBlank_ReturnsNotInstalled(string blank)
    {
        _registry.SetValue(@"SOFTWARE\EqualizerAPO", "ConfigPath", blank);

        var result = CreateService().Detect();

        Assert.False(result.IsInstalled);
    }
}
