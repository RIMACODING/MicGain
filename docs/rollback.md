# Rollback — undoing MicGain's T2.2 system changes (issue #3)

This document covers the system changes made by the T2.2 install flow
(`ApoInstallService` in `MicGain.Core`, wired from `src/MicGain.App/App.xaml.cs`) and how
to undo them. Registry facts follow the VM-verified spec in `docs/research-notes.md` §11
and `docs/internal/apo-development-reference.md` §Registry changes.

## Which path writes what

### 1. Primary path — guided installer + Configurator (`RunGuidedInstallAsync`)

MicGain itself writes **no registry values** on this path. With per-step user consent it:

1. Runs the bundled Equalizer APO installer elevated (`assets/installer/`).
2. Guides the user through the Configurator device selector (the installer launches it;
   `/S` does not suppress it — VM-VERIFIED, research-notes §11). The installer and
   Configurator perform their own registry writes, including their own `Child APOs` backup.
3. Optionally restarts the Windows audio service (`net stop audiosrv && net start audiosrv`).

**Undo**: uninstall Equalizer APO via its own uninstaller (Settings → Apps, or the NSIS
uninstaller in the install directory). The uninstaller restores the original vendor APO
values from its `Child APOs` backup. `NEEDS-VM-VERIFICATION`: complete restore behavior of
the uninstaller across the Win10/Win11 test matrix.

### 2. Advanced/optional path — direct registry writes (`EnableDeviceViaRegistryAsync`)

All writes are under `HKLM`, journaled, and individually consented. Notation:
`{fx}` = `{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}`, `{guid}` = device endpoint GUID.

| Key | Value | Data | Notes |
|---|---|---|---|
| `SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{guid}\FxProperties` | `{fx},5` (REG_SZ) | `{EACD2258-FCAC-4FF4-B36D-419E924A6D79}` | LFX = Equalizer APO [VM-VERIFIED] |
| same | `{fx},6` (REG_SZ) | `{637c490d-eee3-4c0a-973f-371958802da2}` | GFX [VM-VERIFIED] |
| same | `{fx},1` / `{fx},2` (REG_SZ) | same CLSIDs as `,5`/`,6` | only when the device already had `,1`/`,2` (compatibility) |
| same | `{d3993a3f-99c2-4402-b5ec-a92a0367664b},5` and `,6` (REG_MULTI_SZ) | `{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}` | default processing mode [DOC] |
| `SOFTWARE\EqualizerAPO\Child APOs\{guid}` | `{fx},1/2/5/6/7` (REG_SZ) + `Version` = `2` | pre-existing FxProperties values | backup written **before** overwriting; `PreMixChild`/`PostMixChild`/`AllowSilentBufferModification` are NOT written (their default-install data is `NEEDS-VM-VERIFICATION`; no guessing per AGENTS.md) |
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Audio` | `DisableProtectedAudioDG` (REG_DWORD) | `1` | disables the Windows APO signature check **system-wide**; disclosed in the consent prompt (issue #3 AC8) |

## Manual undo (advanced path)

From an elevated prompt or regedit, per affected device:

1. Open the backup key `HKLM\SOFTWARE\EqualizerAPO\Child APOs\{guid}`.
2. Copy each `{fx},N` value back to
   `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{guid}\FxProperties`.
   The `!VALUE` sentinel (seen in `,7` on the test VM) marks a slot with no pre-existing
   value — delete that value from FxProperties instead of copying.
3. Delete the `Child APOs\{guid}` subkey.
4. If no other Equalizer APO usage remains on the machine, delete `DisableProtectedAudioDG`
   from `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio` (or set it to `0`).
5. Restart the Windows audio service (`net stop audiosrv && net start audiosrv`) or reboot.

`NEEDS-VM-VERIFICATION`: full manual-undo walkthrough on a real machine, including the
`!VALUE` sentinel handling and vendor-APO restoration audibility check.

## Automatic rollback (`ApoInstallService.Rollback()`)

On any mid-flow failure in the advanced path the service rolls back automatically
(`InstallOutcome.FailedRolledBack`):

- The write journal is replayed in **reverse order**: each value's previous data is
  restored, and values that did not exist before are deleted.
- The `Child APOs\{guid}` backup subkey is then deleted.

Caveats:

- **Best effort**: individual restore failures are swallowed so the rest of the journal
  still replays. If rollback was incomplete, use the manual procedure above. A stale
  `Child APOs\{guid}` subkey left behind is harmless.
- **DWORD caveat**: a pre-existing `DisableProtectedAudioDG` value journals as absent (the
  registry reader reads strings only) and is therefore deleted on rollback. This is
  acceptable because a pre-existing value implies an Equalizer APO installation for which
  this flow would never have been entered.
- The **primary (guided installer) path** makes no MicGain-owned registry writes, so there
  is nothing for MicGain to roll back there — uninstall Equalizer APO itself instead.
