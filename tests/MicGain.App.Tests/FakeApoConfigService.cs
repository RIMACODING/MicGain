using MicGain.Core.Services;

namespace MicGain.App.Tests;

/// <summary>Records WriteGain payloads so tests assert the exact per-device data sent (issue #4).</summary>
public sealed class FakeApoConfigService : IApoConfigService
{
    public Dictionary<string, double> StoredGains { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string EndpointGuid, double GainDb)> Writes { get; } = new();

    public Exception? ThrowOnWrite { get; set; }

    public double? ReadGain(string endpointGuid) =>
        StoredGains.TryGetValue(endpointGuid, out var gain) ? gain : null;

    public void WriteGain(string endpointGuid, double gainDb)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        Writes.Add((endpointGuid, gainDb));
        StoredGains[endpointGuid] = gainDb;
    }
}
