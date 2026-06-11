# Equalizer APO — Installation tutorial & Troubleshooting (internal canonical copy)

> **Canonical reference for this repository.** Source: Equalizer APO official documentation (wiki), copied here for offline/agent use. Relevant to `ApoInstallService` (T2.x), error states / user guidance in the UI, and diagnostics.

## Installation tutorial

1. Download the Equalizer APO setup for your version of Windows (32 or 64 bit). If you are unsure if you need the 32 or the 64 bit version, you can open Start Menu -> Control Panel -> System and look up the system type.
2. Execute the setup program and follow the instructions. Remember your installation path if you don't use the default of `C:\Program Files\EqualizerAPO`. From here on, for better readability it is assumed that you use the default path.
3. **During the installation process the program `Configurator.exe` will be run.** Make sure that you select the correct audio device to install the APO to. If you are unsure you can open Start Menu -> Control Panel -> Sound and look for the default output device. If you need to install the APO to other audio devices later, you can run the program again from `C:\Program Files\EqualizerAPO\Configurator.exe`.
4. After the installation finished, you should allow the **reboot** of your system. This is needed because the newly installed APO will not be used immediately but only after the **audio service is restarted**.
5. When the system has rebooted, the APO should be active. This will only be noticeable by a small reduction in volume and a mild low frequency boost, because this is what the example configuration file specifies.

## Configuration tutorial

1. Open Windows Explorer and navigate to `C:\Program Files\EqualizerAPO\config`. You should find the files `config.txt` and `example.txt`. **The file `config.txt` is the main configuration file that will automatically be loaded by Equalizer APO.**
2. Open `config.txt` in a text editor and you will see that it first defines a preamplification value and then includes `example.txt`. To check if the APO is really working you can start some audio or video application and adjust the preamp value while music is running. **You should notice that the volume changes immediately each time after you save the file.**
3. To begin creating your individual filter configuration you can install and run Room EQ Wizard (REW). Basic process: Measure -> EQ (equalizer type "Generic" or "FBQ2496" — no other equalizer types are supported) -> EQ Filters (Control: Manual, Type: PK/PEQ, adjust Frequency/Gain/Q) -> save the filter set (REW format) -> File -> Export -> "Filter Settings as text" into `C:\Program Files\EqualizerAPO\config` -> change the `Include` line in `config.txt` to refer to the new file. **The change should be effective immediately.**

## Configuration file format

Moved to the configuration reference — in this repo: `docs/internal/apo-config-reference.md`.

## Troubleshooting

### Configurator

By default, Equalizer APO will try to keep the functionality of other APOs that have been shipped with the sound card driver ("original APOs"). In some cases, this causes instabilities in the audio processing. The Configurator offers **troubleshooting options** to adjust how the original APOs are used.

If you experience instabilities during playback or recording when using Equalizer APO, you can try to disable the usage of the original APOs in the Configurator:

1. Select your audio device by clicking on its connection name.
2. Enable the troubleshooting options.
3. Uncheck both "Use original APO" checkboxes.

Note that you will lose all features that the sound card driver realizes through its APOs. You can also try to uncheck only one of the checkboxes to preserve some functionality.

Some sound card drivers disable options when they detect that another APO has been registered. You can uncheck one of the **"Install APO" checkboxes** to only install Equalizer APO in the **pre-mix or post-mix stage**. For the other stage, the original APO will be registered then, which may help to recover some options of the sound card driver.

### Control Panel

If you installed Equalizer APO and **no changes to the configuration file lead to any changes in the signal, APOs might have been disabled for the device** in the Control Panel. To check this, open Start Menu -> Control Panel -> Sound and double click on your audio device to open the properties dialog.

- If the dialog has an **"Enhancements" tab**: make sure the "Disable all enhancements" checkbox is **unchecked**, even if you don't use any of the enhancements in the list.
- If the dialog does not have an "Enhancements" tab: go to the **"Advanced" tab** and make sure the "Enable audio enhancements" checkbox is **checked**.

### Log files

When Equalizer APO encounters a critical problem while running, it writes a line into the log file:

```
C:\Windows\ServiceProfiles\LocalService\AppData\Local\Temp\EqualizerAPO.log
```

Under normal circumstances, this file does not exist — it is only created when an error occurs.

To get more information, you can enable trace messages (lines marked with "(TRACE)" written even when running normally): open `regedit.exe`, go to `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO` and set the value **`EnableTrace`** to `true`. Then, when playing back or recording audio via a device that Equalizer APO is installed to, information about initialization and the configuration files will be output to the log file. This might help e.g. to see if the configuration files are interpreted as intended. Afterwards, set `EnableTrace` back to `false` so the log file does not grow unnecessarily.

### Hardware-accelerated OpenAL

Normally, applications utilizing OpenAL for their audio output do not present a problem as they will often use DirectSound as their backend, which supports APOs. Some sound card manufacturers however provide OpenAL libraries with hardware-acceleration that access the hardware directly, **circumventing APOs**. There is no way to enable APO support for hardware-accelerated OpenAL; the only solution is to switch to another output library (if the application supports it) or to make OpenAL fall back to software.

- To force OpenAL to fall back to software, `OpenAL32.dll` may be replaced with a different one (e.g. OpenAL Soft).
- A way to globally disable OpenAL hardware-acceleration is to move or rename the vendor-specific OpenAL library in `C:\Windows\System32` or `C:\Windows\SysWOW64`, often named like `*_oal.dll` (e.g. `ct_oal.dll`). **Warning:** this is a modification to the sound driver, not officially supported, and can lead to unexpected results.

---

## Implications for MicGain (maintainer notes, not part of the original reference)

1. **Hot reload confirmed [DOC]**: "the volume changes immediately each time after you save the file" — upgrades `docs/research-notes.md` §2 hot-reload from assumed to documented (capture-device latency still `NEEDS-VM-VERIFICATION`).
2. **Reboot/service-restart requirement confirmed [DOC]**: the APO only activates after the audio service is restarted — validates the consented restart step in T2.2; a full reboot is the documented recommendation, audio-service restart is the mechanism.
3. **Configurator runs during install [DOC]**: the installer launches `Configurator.exe` as part of setup — silent-mode behavior of this step remains the key unknown for unattended install (`NEEDS-VM-VERIFICATION`).
4. **Fresh-install config state [DOC]**: default `config.txt` = a `Preamp:` line + `Include: example.txt`, causing a small volume reduction and mild bass boost. Our marker-region writer must preserve these user-visible defaults untouched; tests should use this exact shape as the "fresh install" fixture.
5. **New failure state for the UI**: "slider does nothing" can be caused by **enhancements disabled in Control Panel** (Enhancements tab "Disable all enhancements" checked, or Advanced tab "Enable audio enhancements" unchecked). Add a troubleshooting hint for this in the app's error/help state (T1.3/T3.1) — detection feasibility via registry: `NEEDS-VM-VERIFICATION`.
6. **Diagnostics hooks [DOC]**: log file at `C:\Windows\ServiceProfiles\LocalService\AppData\Local\Temp\EqualizerAPO.log` (only exists on error) and `HKLM\SOFTWARE\EqualizerAPO\EnableTrace` — useful for a future "diagnostics" feature and for VM verification sessions; also further confirms `HKLM\SOFTWARE\EqualizerAPO` as the product's registry home.
7. **Install path may be non-default [DOC]**: tutorial explicitly allows custom install paths — reinforces: never hardcode `Program Files`, always resolve via `ConfigPath`.
8. **Pre-mix/post-mix install choice exists in Configurator** — device enablement state is not strictly binary; detection (T1.1) should treat "APO present in at least one stage" as enabled. Exact registry representation: `NEEDS-VM-VERIFICATION`.
