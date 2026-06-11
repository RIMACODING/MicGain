namespace MicGain.Core.Services;

/// <summary>
/// Thrown when an Equalizer APO config operation must abort to stay fail-safe
/// (AGENTS.md hard rule 1: malformed input files mean no write, ever).
/// </summary>
public sealed class ApoConfigException : Exception
{
    public ApoConfigException(string message)
        : base(message)
    {
    }

    public ApoConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
