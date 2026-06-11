using Microsoft.Win32;

namespace MicGain.Core.IO;

/// <summary>
/// Production <see cref="IRegistryWriter"/> backed by <see cref="Microsoft.Win32.Registry"/>
/// (BCL only, AGENTS.md stack rules). Windows-only; exercised on real machines, not in CI.
/// HKLM writes throw <see cref="UnauthorizedAccessException"/> when not elevated — the
/// install flow treats that as a failure and rolls back (issue #4 AC4).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsRegistryWriter : IRegistryWriter
{
    public void SetLocalMachineString(string subKeyPath, string valueName, string data)
    {
        using var key = Registry.LocalMachine.CreateSubKey(subKeyPath);
        key.SetValue(valueName, data, RegistryValueKind.String);
    }

    public void SetLocalMachineMultiString(string subKeyPath, string valueName, IReadOnlyList<string> lines)
    {
        using var key = Registry.LocalMachine.CreateSubKey(subKeyPath);
        key.SetValue(valueName, lines.ToArray(), RegistryValueKind.MultiString);
    }

    public void SetLocalMachineDword(string subKeyPath, string valueName, int data)
    {
        using var key = Registry.LocalMachine.CreateSubKey(subKeyPath);
        key.SetValue(valueName, data, RegistryValueKind.DWord);
    }

    public void DeleteLocalMachineValue(string subKeyPath, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteLocalMachineSubKeyTree(string subKeyPath)
    {
        Registry.LocalMachine.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
    }
}
