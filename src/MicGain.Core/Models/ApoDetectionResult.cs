namespace MicGain.Core.Models;

/// <summary>
/// Typed outcome of Equalizer APO global install detection (issue #5, T1.1):
/// <see cref="NotInstalled"/> or <see cref="Installed"/> with the config directory path.
/// </summary>
public sealed class ApoDetectionResult
{
    private ApoDetectionResult(bool isInstalled, string? configPath)
    {
        IsInstalled = isInstalled;
        ConfigPath = configPath;
    }

    public bool IsInstalled { get; }

    /// <summary>
    /// Config directory resolved from <c>HKLM\SOFTWARE\EqualizerAPO</c> value <c>ConfigPath</c>
    /// [DOC]; never a hardcoded <c>Program Files</c> path (research-notes §1).
    /// <c>null</c> when not installed.
    /// </summary>
    public string? ConfigPath { get; }

    public static ApoDetectionResult NotInstalled { get; } = new(false, null);

    public static ApoDetectionResult Installed(string configPath) =>
        new(true, configPath ?? throw new ArgumentNullException(nameof(configPath)));
}
