using Microsoft.Win32;

namespace MicGain.Core.IO;

/// <summary>
/// Production <see cref="IRegistryReader"/> backed by <see cref="Microsoft.Win32.Registry"/>
/// (BCL only, AGENTS.md stack rules). Windows-only; exercised on real machines, not in CI.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsRegistryReader : IRegistryReader
{
    public string? GetLocalMachineString(string subKeyPath, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
        return key?.GetValue(valueName) switch
        {
            string s => s,
            string[] lines => string.Join('\n', lines), // REG_MULTI_SZ
            _ => null,
        };
    }

    public IReadOnlyList<string>? GetLocalMachineSubKeyNames(string subKeyPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
        return key?.GetSubKeyNames();
    }
}
