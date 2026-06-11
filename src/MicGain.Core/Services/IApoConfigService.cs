namespace MicGain.Core.Services;

/// <summary>
/// Per-device gain persistence on top of Equalizer APO config files.
/// See <c>docs/internal/apo-config-reference.md</c> (canonical) and issue #3 (T1.2).
/// </summary>
public interface IApoConfigService
{
    /// <summary>
    /// Reads the gain stored for the given audio endpoint, in dB.
    /// Returns <c>null</c> when the device is not managed by MicGain or its include
    /// file contains no valid <c>Preamp:</c> line (lenient reader, like APO itself).
    /// </summary>
    /// <param name="endpointGuid">Bare endpoint GUID, e.g. <c>{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}</c>.</param>
    double? ReadGain(string endpointGuid);

    /// <summary>
    /// Writes <c>Preamp: &lt;gainDb&gt; dB</c> for the given endpoint into
    /// <c>micgain\{endpoint-guid}.txt</c> and idempotently ensures the marker region in
    /// <c>config.txt</c> references that device. Never touches content outside the
    /// <c># BEGIN micgain</c> / <c># END micgain</c> markers. If the marker state in
    /// <c>config.txt</c> is malformed, throws <see cref="ApoConfigException"/> before
    /// any file is written (fail safe).
    /// </summary>
    /// <param name="endpointGuid">Bare endpoint GUID, e.g. <c>{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}</c>.</param>
    /// <param name="gainDb">Gain in dB. Must be finite; persisted with 0.01 dB precision.</param>
    void WriteGain(string endpointGuid, double gainDb);
}
