using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicGain.App;

/// <summary>
/// Minimal debug-logging utility activated by a <c>--debug</c> command-line flag.
/// Writes timestamped lines to <c>MicGain-debug.log</c> next to the executable.
/// Public surface is intentionally tiny — just <see cref="Enabled"/> and <see cref="WriteLine"/>.
/// </summary>
public static class DebugLog
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "MicGain-debug.log");

    /// <summary>
    /// True once <see cref="Initialize"/> has detected <c>--debug</c> in the command line.
    /// </summary>
    public static bool Enabled { get; private set; }

    /// <summary>
    /// Call once at startup. Parses <c>--debug</c> from the raw command-line args,
    /// writes a header line, and hooks <c>AppDomain.CurrentDomain.UnhandledException</c>
    /// so crash details also land in the log.
    /// </summary>
    public static void Initialize(string[] args)
    {
        Enabled = args.Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));
        if (!Enabled) return;

        try
        {
            File.AppendAllText(LogPath,
                $"{Now()} MicGain debug log started — PID {Environment.ProcessId} — {Environment.OSVersion} — {RuntimeInformation.FrameworkDescription}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort only; never crash because of logging.
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteException(ex, "UnhandledException");
        };
    }

    /// <summary>
    /// Writes a line to the debug log. Cheap no-op when <see cref="Enabled"/> is false.
    /// </summary>
    public static void WriteLine(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        if (!Enabled) return;

        var callerType = Path.GetFileNameWithoutExtension(file);
        try
        {
            File.AppendAllText(LogPath,
                $"{Now()} [{callerType}.{caller}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Swallow — logging must never affect app behavior.
        }
    }

    /// <summary>
    /// Writes an exception to the debug log with full stack trace.
    /// </summary>
    public static void WriteException(Exception ex,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        if (!Enabled) return;

        var callerType = Path.GetFileNameWithoutExtension(file);
        try
        {
            File.AppendAllText(LogPath,
                $"{Now()} [{callerType}.{caller}] EXCEPTION {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
        }
        catch
        {
            // Swallow.
        }
    }

    private static string Now() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
}