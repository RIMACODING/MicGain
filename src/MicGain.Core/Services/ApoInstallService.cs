using MicGain.Core.IO;
using MicGain.Core.Models;

namespace MicGain.Core.Services;

/// <inheritdoc cref="IApoInstallService"/>
/// <remarks>
/// All registry value names and data conform to the VM-verified spec in
/// <c>docs/research-notes.md</c> §11 and <c>docs/internal/apo-development-reference.md</c>
/// §Registry changes. Pure C# behind interfaces — no UI, no direct registry/FS/process
/// access (AGENTS.md rules 3 and 6).
/// </remarks>
public sealed class ApoInstallService : IApoInstallService
{
    /// <summary>Equalizer APO LFX CLSID written to FxProperties <c>,5</c> [VM-VERIFIED] (research-notes §11).</summary>
    public const string EqualizerApoLfxClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";

    /// <summary>Equalizer APO GFX CLSID written to FxProperties <c>,6</c> [VM-VERIFIED] (research-notes §11).</summary>
    public const string EqualizerApoGfxClsid = "{637c490d-eee3-4c0a-973f-371958802da2}";

    /// <summary>Processing-mode property key [DOC] (dev-ref §Registry changes item 4).</summary>
    public const string ProcessingModePropertyKey = "{d3993a3f-99c2-4402-b5ec-a92a0367664b}";

    /// <summary>Default processing mode the <c>,5</c>/<c>,6</c> MULTI_SZ values must contain [DOC].</summary>
    public const string DefaultProcessingMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";

    /// <summary>Configurator's backup key for pre-existing vendor APO values [DOC]/[VM-VERIFIED].</summary>
    public const string ChildAposKeyPath = @"SOFTWARE\EqualizerAPO\Child APOs";

    /// <summary>Key holding <see cref="DisableProtectedAudioDgValueName"/> [DOC] (dev-ref item 1).</summary>
    public const string AudioPolicyKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";

    /// <summary>Disables the APO signature check SYSTEM-WIDE — must be disclosed in the consent UI (issue #4 AC8).</summary>
    public const string DisableProtectedAudioDgValueName = "DisableProtectedAudioDG";

    /// <summary>Child APOs backup covers the <c>,1/2/5/6/7</c> slots [VM-VERIFIED] (research-notes §11).</summary>
    private static readonly int[] ChildApoBackupSuffixes = { 1, 2, 5, 6, 7 };

    private readonly IFileSystem _fileSystem;
    private readonly IRegistryReader _registry;
    private readonly IRegistryWriter _registryWriter;
    private readonly IProcessRunner _processRunner;
    private readonly IInstallInteraction _interaction;
    private readonly string _installerPath;

    public ApoInstallService(
        IFileSystem fileSystem,
        IRegistryReader registry,
        IRegistryWriter registryWriter,
        IProcessRunner processRunner,
        IInstallInteraction interaction,
        string installerPath)
    {
        _fileSystem = fileSystem;
        _registry = registry;
        _registryWriter = registryWriter;
        _processRunner = processRunner;
        _interaction = interaction;
        _installerPath = installerPath;
    }

    // -----------------------------------------------------------------------------------------
    // Primary path: guided Configurator launch
    // -----------------------------------------------------------------------------------------

    public async Task<InstallOutcome> RunGuidedInstallAsync(
        AudioDeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        RequireRenderDevice(device);

        if (string.IsNullOrWhiteSpace(_installerPath) || !_fileSystem.FileExists(_installerPath))
        {
            return InstallOutcome.InstallerNotFound; // fail safe: nothing executed, nothing asked
        }

        // AC2 / AGENTS.md rule 2: installer execution is a system change → explicit consent first.
        if (!await _interaction.ConfirmSystemChangeAsync(
                SystemChange.RunInstaller,
                $"Run the bundled Equalizer APO installer (administrator approval required):\n{_installerPath}"
            ).ConfigureAwait(false))
        {
            return InstallOutcome.ConsentDeclined;
        }

        // /S is accepted but does NOT suppress the Configurator device selector [VM-VERIFIED]
        // (research-notes §11). The exit code under /S is NEEDS-VM-VERIFICATION, so only the
        // UAC-cancel code is interpreted here — enablement is verified authoritatively via
        // the FxProperties check below (AC5), not via the exit code.
        var exitCode = await _processRunner
            .RunElevatedAsync(_installerPath, "/S", cancellationToken)
            .ConfigureAwait(false);

        if (exitCode == IProcessRunner.UacCancelledExitCode)
        {
            return InstallOutcome.ConsentDeclined; // cancelling the UAC prompt = consent withdrawn
        }

        // Primary path (install-ref §Installation tutorial step 3): guide the user through the
        // Configurator selector that the installer launches, and wait for their confirmation.
        await _interaction.WaitForConfiguratorAsync(device).ConfigureAwait(false);

        if (!IsDeviceEnabled(device))
        {
            return InstallOutcome.DeviceNotEnabled; // AC5 check failed — never transition to Ready
        }

        return await OfferAudioServiceRestartAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // Advanced/optional path: direct registry writes
    // -----------------------------------------------------------------------------------------

    public async Task<InstallOutcome> EnableDeviceViaRegistryAsync(
        AudioDeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        RequireRenderDevice(device);

        // AC8: the DisableProtectedAudioDG side effect is disclosed as part of this consent.
        if (!await _interaction.ConfirmSystemChangeAsync(
                SystemChange.WriteRegistry,
                "Enable Equalizer APO for the device by writing its FxProperties registry values, " +
                "back up the existing values to 'Child APOs', and set DisableProtectedAudioDG = 1 " +
                "(disables the Windows APO signature check system-wide)."
            ).ConfigureAwait(false))
        {
            return InstallOutcome.ConsentDeclined;
        }

        var fxKeyPath = FxPropertiesKeyPath(device);
        var backupKeyPath = $@"{ChildAposKeyPath}\{device.EndpointGuid}";
        var journal = new List<RegistryUndo>();

        try
        {
            // 1. Back up pre-existing vendor APO values in Configurator's own format
            //    [VM-VERIFIED] (research-notes §11) so APO's uninstaller restores a clean state
            //    (AC3). PreMixChild / PostMixChild / AllowSilentBufferModification value names
            //    are verified but their data for a default install is not → NOT written here
            //    (NEEDS-VM-VERIFICATION; no guessing per AGENTS.md). Our rollback does not
            //    depend on them.
            foreach (var suffix in ChildApoBackupSuffixes)
            {
                var valueName = $"{AudioDeviceService.FxApoPropertyKey},{suffix}";
                var existing = _registry.GetLocalMachineString(fxKeyPath, valueName);
                if (existing is not null)
                {
                    _registryWriter.SetLocalMachineString(backupKeyPath, valueName, existing);
                }
            }

            _registryWriter.SetLocalMachineDword(backupKeyPath, "Version", 2);

            // 2. FxProperties writes per the VM-verified spec (AC3).
            WriteJournaledString(journal, fxKeyPath,
                $"{AudioDeviceService.FxApoPropertyKey},5", EqualizerApoLfxClsid);
            WriteJournaledString(journal, fxKeyPath,
                $"{AudioDeviceService.FxApoPropertyKey},6", EqualizerApoGfxClsid);

            // Legacy ,1/,2 are written only when the device already had them (research-notes §11:
            // "if device has ,1/,2 entries, also write those for compatibility").
            if (_registry.GetLocalMachineString(fxKeyPath, $"{AudioDeviceService.FxApoPropertyKey},1") is not null)
            {
                WriteJournaledString(journal, fxKeyPath,
                    $"{AudioDeviceService.FxApoPropertyKey},1", EqualizerApoLfxClsid);
            }

            if (_registry.GetLocalMachineString(fxKeyPath, $"{AudioDeviceService.FxApoPropertyKey},2") is not null)
            {
                WriteJournaledString(journal, fxKeyPath,
                    $"{AudioDeviceService.FxApoPropertyKey},2", EqualizerApoGfxClsid);
            }

            // Processing-mode MULTI_SZ values [DOC] (dev-ref §Registry changes item 4).
            WriteJournaledMultiString(journal, fxKeyPath,
                $"{ProcessingModePropertyKey},5", DefaultProcessingMode);
            WriteJournaledMultiString(journal, fxKeyPath,
                $"{ProcessingModePropertyKey},6", DefaultProcessingMode);

            // 3. DisableProtectedAudioDG = 1 [DOC] (dev-ref item 1) — disclosed above (AC8).
            WriteJournaledDword(journal, AudioPolicyKeyPath, DisableProtectedAudioDgValueName, 1);
        }
        catch
        {
            Rollback(journal, backupKeyPath);
            return InstallOutcome.FailedRolledBack;
        }

        return await OfferAudioServiceRestartAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // Detection (AC5)
    // -----------------------------------------------------------------------------------------

    public bool IsDeviceEnabled(AudioDeviceInfo device)
    {
        RequireRenderDevice(device);
        var fxKeyPath = FxPropertiesKeyPath(device);

        // AC5 [VM-VERIFIED]: ,5 takes precedence; ,1 is consulted only when ,5 is absent
        // (dev-ref §Registry changes item 4 precedence rule — mirrors AudioDeviceService).
        var lfx = _registry.GetLocalMachineString(fxKeyPath, $"{AudioDeviceService.FxApoPropertyKey},5")
                  ?? _registry.GetLocalMachineString(fxKeyPath, $"{AudioDeviceService.FxApoPropertyKey},1");

        return lfx is not null &&
               lfx.Contains(EqualizerApoLfxClsid, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private async Task<InstallOutcome> OfferAudioServiceRestartAsync(CancellationToken cancellationToken)
    {
        // The newly registered APO is only used after the Windows Audio service restarts [DOC]
        // (install-ref §Installation tutorial step 4). Whether a service restart alone — no
        // reboot — reliably suffices is NEEDS-VM-VERIFICATION (research-notes §4/§11).
        if (!await _interaction.ConfirmSystemChangeAsync(
                SystemChange.RestartAudioService,
                "Restart the Windows audio service now so Equalizer APO becomes active? " +
                "Audio will cut out briefly."
            ).ConfigureAwait(false))
        {
            return InstallOutcome.SucceededPendingRestart; // never restart silently (AC2)
        }

        var exitCode = await _processRunner
            .RunElevatedAsync("cmd.exe", "/c net stop audiosrv && net start audiosrv", cancellationToken)
            .ConfigureAwait(false);

        return exitCode == IProcessRunner.UacCancelledExitCode
            ? InstallOutcome.SucceededPendingRestart
            : InstallOutcome.Succeeded;
    }

    private void WriteJournaledString(
        List<RegistryUndo> journal, string keyPath, string valueName, string data)
    {
        journal.Add(new RegistryUndo(
            keyPath, valueName,
            _registry.GetLocalMachineString(keyPath, valueName),
            RegistryUndoKind.String));
        _registryWriter.SetLocalMachineString(keyPath, valueName, data);
    }

    private void WriteJournaledMultiString(
        List<RegistryUndo> journal, string keyPath, string valueName, string line)
    {
        journal.Add(new RegistryUndo(
            keyPath, valueName,
            _registry.GetLocalMachineString(keyPath, valueName),
            RegistryUndoKind.MultiString));
        _registryWriter.SetLocalMachineMultiString(keyPath, valueName, new[] { line });
    }

    private void WriteJournaledDword(
        List<RegistryUndo> journal, string keyPath, string valueName, int data)
    {
        // IRegistryReader reads strings only, so a pre-existing DWORD journals as null and is
        // deleted on rollback — acceptable for DisableProtectedAudioDG, because a pre-existing
        // value implies an APO install this flow would never have been entered for.
        // Documented in docs/rollback.md.
        journal.Add(new RegistryUndo(keyPath, valueName, null, RegistryUndoKind.Dword));
        _registryWriter.SetLocalMachineDword(keyPath, valueName, data);
    }

    /// <summary>
    /// AC4: restores previously written values in reverse order, then removes the Child APOs
    /// backup subkey. Best effort — individual restore failures are swallowed so the rest of
    /// the journal still replays; docs/rollback.md documents the manual undo for that case.
    /// </summary>
    private void Rollback(List<RegistryUndo> journal, string backupKeyPath)
    {
        for (var i = journal.Count - 1; i >= 0; i--)
        {
            var undo = journal[i];
            try
            {
                if (undo.PreviousData is null)
                {
                    _registryWriter.DeleteLocalMachineValue(undo.KeyPath, undo.ValueName);
                }
                else if (undo.Kind == RegistryUndoKind.MultiString)
                {
                    _registryWriter.SetLocalMachineMultiString(
                        undo.KeyPath, undo.ValueName, undo.PreviousData.Split('\n'));
                }
                else
                {
                    _registryWriter.SetLocalMachineString(undo.KeyPath, undo.ValueName, undo.PreviousData);
                }
            }
            catch
            {
                // Best effort — continue restoring the remaining entries.
            }
        }

        try
        {
            _registryWriter.DeleteLocalMachineSubKeyTree(backupKeyPath);
        }
        catch
        {
            // Best effort — a stale backup subkey is harmless and documented in docs/rollback.md.
        }
    }

    private static string FxPropertiesKeyPath(AudioDeviceInfo device) =>
        $@"{AudioDeviceService.MmDevicesAudioKeyPath}\Render\{device.EndpointGuid}\FxProperties";

    private static void RequireRenderDevice(AudioDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Flow != DeviceFlow.Render)
        {
            throw new ArgumentException(
                "T2.2 enables render (output) devices only — capture is out of scope for MVP (issue #4).",
                nameof(device));
        }
    }

    private sealed record RegistryUndo(
        string KeyPath,
        string ValueName,
        string? PreviousData,
        RegistryUndoKind Kind);

    private enum RegistryUndoKind { String, MultiString, Dword }
}
