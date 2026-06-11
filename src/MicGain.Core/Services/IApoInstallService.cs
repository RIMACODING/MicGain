using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <summary>
/// Installs Equalizer APO and enables it for one render device (T2.2, issue #4).
/// Canonical references: <c>docs/internal/apo-development-reference.md</c> (FxProperties,
/// Child APOs, DisableProtectedAudioDG), <c>docs/internal/apo-install-troubleshooting-reference.md</c>
/// (install flow, Configurator, restart) and <c>docs/research-notes.md</c> §11 (VM-verified
/// FxProperties spec + Child APOs backup format). Rollback is documented in <c>docs/rollback.md</c>.
/// Capture devices are out of scope for MVP (issue #4) and are rejected.
/// </summary>
public interface IApoInstallService
{
    /// <summary>
    /// Primary path: run the bundled installer elevated, guide the user through the
    /// Configurator device selector (<c>/S</c> does NOT suppress it — VM-verified), verify
    /// enablement via the FxProperties LFX CLSID check (AC5), then offer a consented
    /// audio-service restart. Never performs a system change without consent (AC2).
    /// </summary>
    Task<InstallOutcome> RunGuidedInstallAsync(
        AudioDeviceInfo device,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advanced/optional path: enable the device by replicating Configurator's registry writes
    /// per the VM-verified spec (AC3), with a Child APOs backup first and rollback-on-failure
    /// (AC4). The process must already run elevated for the HKLM writes to succeed.
    /// </summary>
    Task<InstallOutcome> EnableDeviceViaRegistryAsync(
        AudioDeviceInfo device,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC5 [VM-VERIFIED]: <c>true</c> iff the device's FxProperties LFX slot holds the
    /// Equalizer APO CLSID — <c>,5</c> checked first, <c>,1</c> consulted only when <c>,5</c>
    /// is absent (dev-ref precedence rule).
    /// </summary>
    bool IsDeviceEnabled(AudioDeviceInfo device);
}
