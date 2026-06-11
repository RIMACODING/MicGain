using System.ComponentModel;
using System.Diagnostics;

namespace MicGain.Core.IO;

/// <summary>
/// Production <see cref="IProcessRunner"/>. Elevation uses the shell <c>runas</c> verb —
/// the MVP elevation decision for T2.2 (issue #4): a dedicated <c>MicGain.Elevated</c>
/// helper is deferred (AGENTS.md Phase 2). Windows-only; not exercised in CI.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<int> RunElevatedAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true, // required for the runas verb
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == IProcessRunner.UacCancelledExitCode)
        {
            // The user dismissed the UAC prompt — consent withdrawal, not an error.
            return IProcessRunner.UacCancelledExitCode;
        }
    }
}
