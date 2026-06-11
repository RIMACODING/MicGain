namespace MicGain.Core.Audio;

/// <summary>
/// Normalizes Core Audio endpoint IDs of the form <c>{0.0.0.00000000}.{guid}</c> (render) /
/// <c>{0.0.1.00000000}.{guid}</c> (capture) to the bare <c>{guid}</c> used as the registry key
/// under <c>MMDevices\Audio\{Render|Capture}</c> and as <c>Device:</c> selector.
/// See <c>docs/research-notes.md</c> §5 "Endpoint GUID normalization" [DOC].
/// </summary>
public static class CoreAudioEndpointId
{
    /// <summary>
    /// Extracts the trailing <c>{guid}</c> from a Core Audio device ID. Returns <c>false</c>
    /// (and an empty <paramref name="endpointGuid"/>) when the ID does not end in a valid
    /// braced GUID — callers must fail safe and skip such devices, never guess registry paths.
    /// The result is normalized to lowercase braced form.
    /// </summary>
    public static bool TryExtractEndpointGuid(string? coreAudioId, out string endpointGuid)
    {
        endpointGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(coreAudioId) || !coreAudioId.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        var start = coreAudioId.LastIndexOf('{');
        if (start < 0)
        {
            return false;
        }

        if (!Guid.TryParseExact(coreAudioId[start..], "B", out var guid))
        {
            return false;
        }

        endpointGuid = guid.ToString("B"); // lowercase, braced — matches registry key casing conventions
        return true;
    }
}
