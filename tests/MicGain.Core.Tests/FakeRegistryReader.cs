using MicGain.Core.IO;

namespace MicGain.Core.Tests;

/// <summary>In-memory <see cref="IRegistryReader"/> so tests run without a Windows registry (AGENTS.md rule 3).</summary>
public sealed class FakeRegistryReader : IRegistryReader
{
    /// <summary>Key path → (value name → data). Case-insensitive like the real registry.</summary>
    public Dictionary<string, Dictionary<string, string>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void SetValue(string subKeyPath, string valueName, string data)
    {
        if (!Keys.TryGetValue(subKeyPath, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Keys[subKeyPath] = values;
        }

        values[valueName] = data;
    }

    public string? GetLocalMachineString(string subKeyPath, string valueName) =>
        Keys.TryGetValue(subKeyPath, out var values) && values.TryGetValue(valueName, out var data)
            ? data
            : null;

    public IReadOnlyList<string>? GetLocalMachineSubKeyNames(string subKeyPath)
    {
        var prefix = subKeyPath.TrimEnd('\\') + "\\";
        var children = Keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (children.Count > 0)
        {
            return children;
        }

        // Key with values but no subkeys → empty list; key absent entirely → null.
        return Keys.ContainsKey(subKeyPath) ? children : null;
    }
}
