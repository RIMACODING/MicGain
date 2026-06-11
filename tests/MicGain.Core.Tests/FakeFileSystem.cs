using MicGain.Core.IO;

namespace MicGain.Core.Tests;

/// <summary>In-memory <see cref="IFileSystem"/> so tests run without Windows or Equalizer APO (AGENTS.md rule 3).</summary>
public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public HashSet<string> CreatedDirectories { get; } = new(StringComparer.Ordinal);

    /// <summary>Directories that "exist" for <see cref="DirectoryExists"/>.</summary>
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every path passed to <see cref="WriteAllText"/>, in order — used to assert "no write" fail-safe behavior.</summary>
    public List<string> WrittenPaths { get; } = new();

    public bool FileExists(string path) => Files.ContainsKey(path);

    public string ReadAllText(string path) =>
        Files.TryGetValue(path, out var content)
            ? content
            : throw new FileNotFoundException("File not found.", path);

    public void WriteAllText(string path, string contents)
    {
        Files[path] = contents;
        WrittenPaths.Add(path);
    }

    public bool DirectoryExists(string path) =>
        Directories.Contains(path) || CreatedDirectories.Contains(path);

    public void CreateDirectory(string path) => CreatedDirectories.Add(path);
}
