namespace MicGain.Core.Models;

/// <summary>
/// Allowable <c>Preamp:</c> gain range exposed to the UI (MAIN PLAN T1.3: −30…+15 dB, default 0).
/// The positive cap is deliberately conservative: config-ref documents
/// <c>Preamp: &lt;Negative number&gt; dB</c> and acceptance of positive values is
/// NEEDS-VM-VERIFICATION (docs/research-notes.md §2).
/// </summary>
public static class GainRange
{
    public const double MinDb = -30.0;

    public const double MaxDb = 15.0;

    public const double DefaultDb = 0.0;

    /// <summary>Clamps to the allowed range; NaN falls back to <see cref="DefaultDb"/> (fail safe).</summary>
    public static double Clamp(double gainDb) =>
        double.IsNaN(gainDb) ? DefaultDb : Math.Clamp(gainDb, MinDb, MaxDb);
}
