# Smoke checklist — MicGain MVP

> Manual integration test matrix. Per AGENTS.md, anything that requires a real Windows
> machine with Equalizer APO is marked `NEEDS-VM-VERIFICATION` — confirm on a fresh Windows
> VM before shipping. Run through this list after every build before distributing.

## Pre-flight: build and test

```sh
sqz dotnet restore MicGain.sln
sqz dotnet build  MicGain.sln -c Release
sqz dotnet test tests/MicGain.Core.Tests/MicGain.Core.Tests.csproj -c Release
sqz dotnet test tests/MicGain.App.Tests/MicGain.App.Tests.csproj -c Release
```

## Pre-flight: publish single-file

```sh
sqz dotnet publish src/MicGain.App/MicGain.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Verify `publish/` folder contains `assets/installer/EqualizerAPO-x64-1.4.2.exe`,
`assets/installer/README.md`, and the published `.exe`.

## Scenario 1 — Clean machine: install consent → guided install → ready

**Pre-state**: Windows 10 or 11, **no Equalizer APO installed** (Settings → Apps or
`HKLM\SOFTWARE\EqualizerAPO` absent). At least one active output device present.

| Step | Action | Expected | ✓ |
|---|---|---|---|
| 1.1 | Copy `publish/` folder to the machine. Run `MicGain.App.exe`. | Install consent dialog opens, names the default output device by its friendly name. | |
| 1.2 | Click "Decline" or close the dialog. | App exits cleanly. Zero system changes: `HKLM\SOFTWARE\EqualizerAPO` still absent. | |
| 1.3 | Run again, click "Install". | UAC prompt for administrator rights. | |
| 1.4 | Cancel the UAC prompt. | "Installation was cancelled: consent was not given" message. Clean exit. | |
| 1.5 | Run again, approve UAC. | Installer runs; Configurator device selector opens. | |
| 1.6 | Select default output device in Configurator, click Close. Configurator closes; click "Done" on MicGain's "Wait for Configurator" dialog. | — | |
| 1.7 | Service restart consent dialog. Click "No" (decline). | MessageBox: "APO becomes active after Windows audio service restarts or reboot." Main window opens; devices show "APO is not enabled on this device". | |
| 1.8 | Click Refresh. | Devices still greyed out (pending service restart). | |
| 1.9 | Reboot. Run MicGain again. | Main window opens directly. APO-enabled capture devices show active sliders. Move slider → audible gain change. `NEEDS-VM-VERIFICATION`. | |
| 1.10 | Repeat the install flow, this time accept the service restart consent. | Audio cuts out briefly, then restores. Main window opens. APO-enabled capture devices show active sliders immediately — no reboot needed. | |

## Scenario 2 — APO already installed (steady state)

**Pre-state**: Equalizer APO installed and enabled on at least one capture device.

| Step | Action | Expected | ✓ |
|---|---|---|---|
| 2.1 | Run `MicGain.App.exe`. | Main window opens directly (no consent dialog). Lists capture devices with friendly name, GUID, and gain slider. | |
| 2.2 | APO-enabled devices show active sliders; non-APO-enabled show "not enabled" greyed out. | Correct behavior. | |
| 2.3 | Move slider on an APO-enabled device. | Gain value updates live on the label. Audible change within ~1 s. `NEEDS-VM-VERIFICATION`. | |
| 2.4 | Rapid slider movement for 60 s. | No config corruption, no APO crash, final value persisted after debounce. | |
| 2.5 | Close and reopen the app. | Each device shows its last-saved gain value. | |
| 2.6 | Change gain on device A, switch to device B. | Device B loads its own stored gain — no cross-device leakage. | |
| 2.7 | Inspect `config.txt` (at `ConfigPath`) before and after slider changes. Diff the file. | Only `# BEGIN micgain` / `# END micgain` marker region changes. User content outside markers untouched. | |
| 2.8 | Inspect per-device include file (`micgain\{guid}.txt`). | Contains a single `Preamp: X dB` line. | |

## Scenario 3 — No active output device / no capture device

| Step | Action | Expected | ✓ |
|---|---|---|---|
| 3.1 | APO not installed, no active output device. Run app. | Consent dialog shows "No active output device detected." Click OK → clean exit. `NEEDS-VM-VERIFICATION` (edge case: disabled vs unplugged). | |
| 3.2 | APO installed, zero capture devices. Run app. | Main window shows "No capture devices (microphones) were detected." | |

## Scenario 4 — Two devices with identical names

**Pre-state**: Two capture devices with the same friendly name (e.g. two identical USB mics),
both APO-enabled.

| Step | Action | Expected | ✓ |
|---|---|---|---|
| 4.1 | Run app. Wait for Ready state. | Both devices appear as separate rows. Their GUIDs differentiate them. | |
| 4.2 | Set gain to +3 dB on device A, −6 dB on device B. | Each device's include file contains its own Preamp value. No cross-contamination. | |

## Scenario 5 — Installer binary missing

**Pre-state**: APO not installed. Delete `assets/installer/EqualizerAPO-x64-1.4.2.exe` from the publish folder.

| Step | Action | Expected | ✓ |
|---|---|---|---|
| 5.1 | Run the app. | App detects APO is missing, enters consent flow. Run through consent prompts. | |
| 5.2 | Consent given, install attempted. | MessageBox: "The bundled Equalizer APO installer was not found: … Nothing was executed and no changes were made." Clean exit. | |

## Scenario 6 — Malformed config.txt

**Pre-state**: APO installed. Manually corrupt `config.txt` (e.g. remove a closing marker).

| Step | Action | Expected | ✓ |
|---|---|---|---|
| 6.1 | Run the app. | Main window opens. On first slider move, status line shows an error. Config file is never written in its malformed state — fail safe per AGENTS.md rule 1. | |

## Non-destructiveness check

After any scenario that writes config:

1. Snapshot `config.txt` (and any `micgain\*.txt`) before the run.
2. Diff afterward.
3. **Pass** if: only `# BEGIN micgain` / `# END micgain` regions changed; user content outside markers is identical.
4. **Fail** if: any line outside markers was modified or deleted.