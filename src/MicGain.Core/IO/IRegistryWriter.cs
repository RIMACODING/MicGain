namespace MicGain.Core.IO;

/// <summary>
/// Write-side registry abstraction for the T2.2 install flow (issue #4), kept separate from
/// <see cref="IRegistryReader"/> so read-only consumers keep least privilege (AGENTS.md rule 4).
/// All paths are relative to <c>HKEY_LOCAL_MACHINE</c>. HKLM writes require elevation —
/// production callers run on the elevated install path only, never on the slider path.
/// Behind an interface so unit tests run with mocks (AGENTS.md rule 3 — CI has no Windows
/// registry).
/// </summary>
public interface IRegistryWriter
{
    /// <summary>Writes a <c>REG_SZ</c> value, creating the key if needed.</summary>
    void SetLocalMachineString(string subKeyPath, string valueName, string data);

    /// <summary>Writes a <c>REG_MULTI_SZ</c> value, creating the key if needed.</summary>
    void SetLocalMachineMultiString(string subKeyPath, string valueName, IReadOnlyList<string> lines);

    /// <summary>Writes a <c>REG_DWORD</c> value, creating the key if needed.</summary>
    void SetLocalMachineDword(string subKeyPath, string valueName, int data);

    /// <summary>Deletes a value; no-op when the key or value does not exist.</summary>
    void DeleteLocalMachineValue(string subKeyPath, string valueName);

    /// <summary>Deletes a subkey and all its children; no-op when it does not exist.</summary>
    void DeleteLocalMachineSubKeyTree(string subKeyPath);
}
