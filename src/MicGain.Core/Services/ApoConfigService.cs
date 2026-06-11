using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MicGain.Core.IO;

namespace MicGain.Core.Services;

/// <summary>
/// Reads and writes per-device gain (<c>Preamp:</c>) in Equalizer APO config files.
/// Conforms to <c>docs/internal/apo-config-reference.md</c> (canonical — do not change
/// emitted syntax without consulting it).
///
/// Layout (docs/research-notes.md §2):
/// <code>
/// # BEGIN micgain
/// Device: {endpoint-guid-1}
/// Include: micgain\{endpoint-guid-1}.txt
/// Device: all
/// # END micgain
/// </code>
/// Invariants:
/// 1. Only the marker region of config.txt is ever rewritten; all other bytes are preserved exactly.
/// 2. The region always ends with <c>Device: all</c> — a Device: line scopes everything until the
///    next Device: line (config-ref §Device), so this reset prevents leaking our device scope
///    into user content below the markers.
/// 3. Writer is strict and emits only plain literal lines; it never emits backticks, the
///    inline-expression delimiter (config-ref §Eval and inline expressions; maintainer notes 8–9).
/// 4. Reader is lenient: APO silently ignores non-conforming lines, and so do we.
/// 5. Fail safe: malformed marker state or a missing config.txt aborts before any write.
/// </summary>
public sealed class ApoConfigService : IApoConfigService
{
    public const string BeginMarker = "# BEGIN micgain";
    public const string EndMarker = "# END micgain";

    private const string ConfigFileName = "config.txt";
    private const string IncludeDirectoryName = "micgain";

    // Lenient reader: matches "Preamp: <number> dB" with any casing/whitespace.
    // Writer always emits InvariantCulture '.' decimals; locale acceptance by APO is
    // NEEDS-VM-VERIFICATION (research-notes §2), so the reader only accepts '.' too.
    private static readonly Regex PreampLineRegex = new(
        @"^\s*Preamp:\s*([+-]?[0-9]+(?:\.[0-9]+)?)\s*dB\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Only braced endpoint GUIDs are recognized as ours inside the marker region;
    // "Device: all" intentionally does not match.
    private static readonly Regex DeviceGuidLineRegex = new(
        @"^\s*Device:\s*(\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\})\s*$",
        RegexOptions.Compiled);

    // Lookahead (instead of consuming \r?\n) so the replacement span keeps the original
    // line terminator after the END marker — guarantees byte-exact preservation outside markers.
    private static readonly Regex BeginMarkerRegex = new(
        @"^[ \t]*# BEGIN micgain[ \t]*(?=\r?\n|\z)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex EndMarkerRegex = new(
        @"^[ \t]*# END micgain[ \t]*(?=\r?\n|\z)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly IFileSystem _fileSystem;
    private readonly string _configDirectory;

    /// <param name="fileSystem">Filesystem abstraction (mocked in tests).</param>
    /// <param name="configDirectory">Equalizer APO config directory, resolved from the registry by detection (T1.1) — never hardcoded.</param>
    public ApoConfigService(IFileSystem fileSystem, string configDirectory)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _configDirectory = string.IsNullOrWhiteSpace(configDirectory)
            ? throw new ArgumentException("Config directory must be provided.", nameof(configDirectory))
            : configDirectory;
    }

    public double? ReadGain(string endpointGuid)
    {
        var guid = NormalizeEndpointGuid(endpointGuid);
        var includePath = GetIncludeFilePath(guid);
        if (!_fileSystem.FileExists(includePath))
        {
            return null;
        }

        foreach (var line in SplitLines(_fileSystem.ReadAllText(includePath)))
        {
            var match = PreampLineRegex.Match(line);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        // Lenient: no valid Preamp line — treat as "no stored gain" rather than failing.
        return null;
    }

    public void WriteGain(string endpointGuid, double gainDb)
    {
        var guid = NormalizeEndpointGuid(endpointGuid);
        if (!double.IsFinite(gainDb))
        {
            throw new ArgumentOutOfRangeException(nameof(gainDb), gainDb, "Gain must be a finite number of dB.");
        }

        // 1) Read and validate config.txt BEFORE any write (fail safe, AGENTS.md hard rule 1).
        var configPath = Path.Combine(_configDirectory, ConfigFileName);
        if (!_fileSystem.FileExists(configPath))
        {
            throw new ApoConfigException(
                $"'{configPath}' was not found. Refusing to create an Equalizer APO config file from scratch; " +
                "the config directory is probably wrong or the APO install is broken.");
        }

        var originalConfig = _fileSystem.ReadAllText(configPath);
        var updatedConfig = ComputeUpdatedConfig(originalConfig, guid); // throws ApoConfigException when malformed
        var includeContent = FormatPreampLine(gainDb) + "\r\n";

        // 2) Write the per-device include file (the actual gain value).
        _fileSystem.CreateDirectory(Path.Combine(_configDirectory, IncludeDirectoryName));
        _fileSystem.WriteAllText(GetIncludeFilePath(guid), includeContent);

        // 3) Update config.txt only when the marker region actually changed (idempotent:
        //    repeated writes for an already-managed device never rewrite config.txt).
        if (!string.Equals(updatedConfig, originalConfig, StringComparison.Ordinal))
        {
            _fileSystem.WriteAllText(configPath, updatedConfig);
        }
    }

    /// <summary>
    /// Returns the new config.txt content with the marker region created or rebuilt so it
    /// references <paramref name="guid"/>. All content outside the markers is preserved byte-exactly.
    /// Throws <see cref="ApoConfigException"/> when the marker state is malformed.
    /// </summary>
    private static string ComputeUpdatedConfig(string original, string guid)
    {
        var beginMatches = BeginMarkerRegex.Matches(original);
        var endMatches = EndMarkerRegex.Matches(original);
        var eol = DetectEol(original);

        if (beginMatches.Count == 0 && endMatches.Count == 0)
        {
            // Fresh config (e.g. the default install fixture: "Preamp:" + "Include: example.txt"):
            // append our region at the end, leaving everything above untouched.
            var sb = new StringBuilder(original);
            if (original.Length > 0 && !original.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.Append(eol);
            }

            sb.Append(BuildMarkerRegion(new[] { guid }, eol)).Append(eol);
            return sb.ToString();
        }

        if (beginMatches.Count != 1 || endMatches.Count != 1 || endMatches[0].Index <= beginMatches[0].Index)
        {
            throw new ApoConfigException(
                "Malformed micgain marker region in config.txt (unbalanced, duplicated or out-of-order " +
                $"'{BeginMarker}' / '{EndMarker}' markers). Refusing to write to avoid corrupting the file.");
        }

        var begin = beginMatches[0];
        var end = endMatches[0];

        var regionInnerStart = begin.Index + begin.Length;
        var regionInner = original[regionInnerStart..end.Index];

        var managedGuids = ExtractManagedGuids(regionInner);
        if (!managedGuids.Contains(guid))
        {
            managedGuids.Add(guid);
        }

        var rebuiltRegion = BuildMarkerRegion(managedGuids, eol);
        return original[..begin.Index] + rebuiltRegion + original[(end.Index + end.Length)..];
    }

    /// <summary>Extracts already-managed endpoint GUIDs from the marker region, preserving order.</summary>
    private static List<string> ExtractManagedGuids(string regionInner)
    {
        var guids = new List<string>();
        foreach (var line in SplitLines(regionInner))
        {
            var match = DeviceGuidLineRegex.Match(line);
            if (match.Success && Guid.TryParse(match.Groups[1].Value, out var parsed))
            {
                var normalized = parsed.ToString("B");
                if (!guids.Contains(normalized))
                {
                    guids.Add(normalized);
                }
            }
        }

        return guids;
    }

    private static string BuildMarkerRegion(IReadOnlyList<string> guids, string eol)
    {
        var sb = new StringBuilder();
        sb.Append(BeginMarker).Append(eol);
        foreach (var guid in guids)
        {
            sb.Append("Device: ").Append(guid).Append(eol);
            sb.Append("Include: ").Append(IncludeDirectoryName).Append('\\').Append(guid).Append(".txt").Append(eol);
        }

        // REQUIRED scope reset (config-ref §Device, maintainer note 2): a Device: line scopes
        // every following command until the next Device: line, so without this the last
        // device's scope would leak into user content below our markers.
        sb.Append("Device: all").Append(eol);
        sb.Append(EndMarker);

        var region = sb.ToString();
        EnsureNoInlineExpressionDelimiters(region);
        return region;
    }

    private static string FormatPreampLine(double gainDb)
    {
        // Strict writer: plain literal line, InvariantCulture '.' decimal, max 2 decimals.
        // Positive values are accepted here but their on-device acceptance is
        // NEEDS-VM-VERIFICATION (config-ref documents "<Negative number> dB"); the UI caps
        // the positive range conservatively (T1.3).
        var line = "Preamp: " + gainDb.ToString("0.##", CultureInfo.InvariantCulture) + " dB";
        EnsureNoInlineExpressionDelimiters(line);
        return line;
    }

    private static void EnsureNoInlineExpressionDelimiters(string generatedContent)
    {
        // Defense in depth for config-ref maintainer note 9: a stray backtick would be
        // parsed by APO as an inline-expression delimiter.
        if (generatedContent.Contains('`'))
        {
            throw new ApoConfigException("Generated content must never contain backticks (inline-expression delimiter).");
        }
    }

    private static string NormalizeEndpointGuid(string endpointGuid)
    {
        // The bare {guid} is what AudioDeviceService extracts from IMMDevice::GetId
        // (research-notes §5, endpoint GUID normalization). Normalizing to braced
        // lowercase keeps Device: selectors and include file names canonical, and
        // guarantees the value is filesystem-safe.
        if (string.IsNullOrWhiteSpace(endpointGuid) || !Guid.TryParse(endpointGuid.Trim(), out var guid))
        {
            throw new ArgumentException($"'{endpointGuid}' is not a valid audio endpoint GUID.", nameof(endpointGuid));
        }

        return guid.ToString("B");
    }

    private string GetIncludeFilePath(string normalizedGuid) =>
        Path.Combine(_configDirectory, IncludeDirectoryName, normalizedGuid + ".txt");

    private static string DetectEol(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : text.Contains('\n') ? "\n"
        : "\r\n";

    private static IEnumerable<string> SplitLines(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            yield return line.TrimEnd('\r');
        }
    }
}
