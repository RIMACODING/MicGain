namespace MicGain.Core.IO;

/// <summary>
/// Process-launch abstraction so the install flow is unit-testable (AGENTS.md rule 3).
/// The single elevated entry point keeps elevation confined to the install path
/// (AGENTS.md rule 4 — the app manifest stays <c>asInvoker</c>).
/// </summary>
public interface IProcessRunner
{
    /// <summary>Win32 <c>ERROR_CANCELLED</c> — reported when the user cancels the UAC prompt.</summary>
    const int UacCancelledExitCode = 1223;

    /// <summary>
    /// Launches a process elevated (UAC prompt) and waits for it to exit. Returns the process
    /// exit code, or <see cref="UacCancelledExitCode"/> when the user cancels the UAC prompt
    /// (treated by callers as consent withdrawal, never as an error).
    /// </summary>
    Task<int> RunElevatedAsync(string fileName, string arguments, CancellationToken cancellationToken = default);
}
