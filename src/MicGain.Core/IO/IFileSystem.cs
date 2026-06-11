namespace MicGain.Core.IO;

/// <summary>
/// Filesystem abstraction so <c>MicGain.Core</c> services stay unit-testable with mocks
/// (AGENTS.md rule 3 — CI has no Windows audio or Equalizer APO install).
/// </summary>
public interface IFileSystem
{
    /// <summary>Returns true when a file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>Reads the entire file as text.</summary>
    string ReadAllText(string path);

    /// <summary>Writes <paramref name="contents"/> to the file, replacing it if it exists.</summary>
    void WriteAllText(string path, string contents);

    /// <summary>Returns true when a directory exists at <paramref name="path"/>.</summary>
    bool DirectoryExists(string path);

    /// <summary>Creates the directory (and parents) if it does not exist.</summary>
    void CreateDirectory(string path);
}
