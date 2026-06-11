using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <summary>
/// Detects whether Equalizer APO is installed globally (issue #5, T1.1).
/// Canonical references: <c>docs/internal/apo-development-reference.md</c> and
/// <c>docs/research-notes.md</c> §1.
/// </summary>
public interface IApoDetectionService
{
    /// <summary>
    /// Primary check: <c>HKLM\SOFTWARE\EqualizerAPO</c> string value <c>ConfigPath</c> [DOC].
    /// Secondary sanity check: the config directory exists on disk.
    /// Fail safe: a present registry value with a missing directory reports
    /// <see cref="ApoDetectionResult.NotInstalled"/>.
    /// </summary>
    ApoDetectionResult Detect();
}
