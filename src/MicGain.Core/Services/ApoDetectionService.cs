using MicGain.Core.IO;
using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <inheritdoc cref="IApoDetectionService"/>
public sealed class ApoDetectionService : IApoDetectionService
{
    /// <summary>
    /// Equalizer APO's registry home [DOC] (config-ref §Expression commands;
    /// install-ref §Log files). Exact value data on real installs: NEEDS-VM-VERIFICATION.
    /// </summary>
    public const string EqualizerApoKeyPath = @"SOFTWARE\EqualizerAPO";

    public const string ConfigPathValueName = "ConfigPath";

    private readonly IRegistryReader _registry;
    private readonly IFileSystem _fileSystem;

    public ApoDetectionService(IRegistryReader registry, IFileSystem fileSystem)
    {
        _registry = registry;
        _fileSystem = fileSystem;
    }

    public ApoDetectionResult Detect()
    {
        var configPath = _registry.GetLocalMachineString(EqualizerApoKeyPath, ConfigPathValueName);
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return ApoDetectionResult.NotInstalled;
        }

        // Sanity check (research-notes §1): the registry value may be stale (e.g. manual
        // deletion of the install dir). Fail safe — never report Installed with a config
        // path that does not exist on disk. Custom install paths are supported, so the
        // path is always taken from the registry, never hardcoded [DOC].
        if (!_fileSystem.DirectoryExists(configPath))
        {
            return ApoDetectionResult.NotInstalled;
        }

        return ApoDetectionResult.Installed(configPath);
    }
}
