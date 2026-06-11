using MicGain.Core.IO;

namespace MicGain.Core.Tests;

/// <summary>
/// In-memory <see cref="IRegistryWriter"/> mutating the same store as a
/// <see cref="FakeRegistryReader"/>, so service reads observe prior writes
/// (AGENTS.md rule 3 — no Windows registry in CI). MULTI_SZ data is stored joined by
/// <c>'n'</c> and DWORD data as its invariant string, mirroring the reader's conventions.
/// </summary>
public sealed class FakeRegistryWriter : IRegistryWriter
{
    private readonly FakeRegistryReader _store;
    private int _writeCount;

    public FakeRegistryWriter(FakeRegistryReader store) => _store = store;

    /// <summary>1-based write number that throws once — used to exercise rollback-on-failure.</summary>
    public int? ThrowOnWriteNumber { get; set; }

    /// <summary>Every <c>keyvalue</c> passed to <see cref="DeleteLocalMachineValue"/>, in order.</summary>
    public List<string> DeletedValues { get; } = new();

    /// <summary>Every subkey path passed to <see cref="DeleteLocalMachineSubKeyTree"/>, in order.</summary>
    public List<string> DeletedTrees { get; } = new();

    public void SetLocalMachineString(string subKeyPath, string valueName, string data) =>
        Write(subKeyPath, valueName, data);

    public void SetLocalMachineMultiString(string subKeyPath, string valueName, IReadOnlyList<string> lines) =>
        Write(subKeyPath, valueName, string.Join('n', lines));

    public void SetLocalMachineDword(string subKeyPath, string valueName, int data) =>
        Write(subKeyPath, valueName, data.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void DeleteLocalMachineValue(string subKeyPath, string valueName)
    {
        DeletedValues.Add($@"{subKeyPath}{valueName}");
        if (_store.Keys.TryGetValue(subKeyPath, out var values))
        {
            values.Remove(valueName);
        }
    }

    public void DeleteLocalMachineSubKeyTree(string subKeyPath)
    {
        DeletedTrees.Add(subKeyPath);
        var doomed = _store.Keys.Keys
            .Where(k => k.Equals(subKeyPath, StringComparison.OrdinalIgnoreCase)
                        || k.StartsWith(subKeyPath + "", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in doomed)
        {
            _store.Keys.Remove(key);
        }
    }

    private void Write(string subKeyPath, string valueName, string data)
    {
        _writeCount++;
        if (_writeCount == ThrowOnWriteNumber)
        {
            throw new UnauthorizedAccessException("Simulated registry write failure (test).");
        }

        _store.SetValue(subKeyPath, valueName, data);
    }
}