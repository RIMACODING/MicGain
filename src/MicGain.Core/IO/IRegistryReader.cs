namespace MicGain.Core.IO;

/// <summary>
/// Read-only registry abstraction so <c>MicGain.Core</c> services stay unit-testable with mocks
/// (AGENTS.md rules 3 and 6 — CI has no Windows registry state). All paths are relative to
/// <c>HKEY_LOCAL_MACHINE</c>. Read-only by design: T1.1 never writes to the registry
/// (least privilege, AGENTS.md rule 4).
/// </summary>
public interface IRegistryReader
{
    /// <summary>
    /// Reads a value under HKLM. Returns <c>null</c> when the key or value does not exist.
    /// Pass an empty <paramref name="valueName"/> to read a key's default value.
    /// <c>REG_MULTI_SZ</c> data is returned with lines joined by <c>'\n'</c>.
    /// </summary>
    string? GetLocalMachineString(string subKeyPath, string valueName);

    /// <summary>
    /// Names of the immediate subkeys of an HKLM key, or <c>null</c> when the key does not exist.
    /// </summary>
    IReadOnlyList<string>? GetLocalMachineSubKeyNames(string subKeyPath);
}
