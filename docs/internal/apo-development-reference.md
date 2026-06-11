# Equalizer APO — Compilation & APO development (internal canonical copy)

> **Canonical developer reference for this repository.** Source: Equalizer APO official developer documentation (wiki), copied here for offline/agent use. All code touching APO registration (detection, device enablement, rollback — `ApoDetectionService`, `ApoInstallService`) MUST follow this document, not memory or external sources.

## Compilation prerequisites

The following software has been successfully used to compile Equalizer APO:

- **Visual Studio Community 2019.** Unfortunately, downloads of non-current versions of Visual Studio are only possible with an MSDN or (free) Dev Essentials membership.
- **libsndfile.** There are installers for the 64 and 32 bit version, so no need to compile from source. Has to be installed to `C:\Program Files\libsndfile` and `C:\Program Files (x86)\libsndfile`, respectively.
- **FFTW.** The prebuilt 64/32 bit archives have to be extracted to `C:\Program Files\fftw3` and `C:\Program Files (x86)\fftw3`, respectively. The import libraries have to be created using the `lib` program of Visual Studio.
- **muParserX 3.0.1.** No prebuilt files, so compilation from source is needed. The version has to be **3.0.1** as an important feature was removed in 3.0.2 (semicolon). As version 3.0.1 is no longer available, it is attached to the original wiki page including prebuilt static library files for MSVC2013. The zip file has to be extracted to `C:\Program Files`.
- **TCLAP.** A template library; only the source is needed, compiled into the application. The downloaded tar.gz has to be extracted to `C:\Program Files`. Only used in the Benchmark application.
- **Qt 5.** The 32 and 64 bit versions for MSVC2019 should be installed to `C:\Qt`. Qt Creator is used instead of Visual Studio as IDE. Prebuilt versions can be used for development although Equalizer APO ships with custom-built variants with reduced dependencies. Currently only needed to build the Configuration Editor.
- **NSIS.** Needed to create the installer. Additionally, the plugins **NSISpcre**, **AccessControl** and **nsArray** are needed.

## Source code organization

The Equalizer APO project consists of five parts:

1. **EqualizerAPO.** The main project, which generates the Audio Processing Object DLL, `EqualizerAPO.dll`. It contains the boilerplate code for COM and implements the APO interfaces, calling the class `ParametricEQ`, which contains the actual filtering algorithm and is also used by the Benchmark project.
2. **Configurator.** The GUI utility which is called during the setup process to allow the user to select the audio devices for which the APO should be registered.
3. **Benchmark.** A console program to test the audio processing implementation without actually installing it for an audio device. Handy when experimenting with new filter types or tuning existing ones, especially to evaluate performance.
4. **Editor.** The Qt Creator project that builds the Configuration Editor.
5. **Setup.** Not a Visual Studio project, but a set of NSIS scripts and additional files used to create the installers.

The file `build.bat` in the top-level directory uses msbuild to build all three Visual Studio projects for both 32 and 64 bit, Qt's build tools to build the Configuration Editor for 32 and 64 bit, and then calls NSIS to create both installers.

## APO development

An APO (Audio Processing Object) is a user-space program module that is loaded by the Windows Audio Service to process the audio sample data before it is sent to the audio device driver. APOs are normally distributed and installed together with the audio driver and have to be signed to make sure that they don't circumvent any audio-related DRM measures. There are two kinds of APOs: **GFX** (global effect, applied after mixing the audio streams together) and **LFX** (local effect, applied before mixing). Only one GFX and one LFX APO can be registered for an output device and only one LFX APO can be registered for an input device. An APO is implemented as a COM object which is assigned a GUID under which it is registered in the system registry. More detailed information and code samples can be found in the documents *Custom Audio Effects in Windows Vista* and *Reusing Windows Vista Audio System Effects*.

To add a custom APO to an audio device, two obstacles have to be overcome: the audio engine has to be configured to allow unsigned APOs, and the existing APO assigned to the audio device has to be attached to the custom APO, so that it can still process the audio data.

### Registry changes required to load the APO DLL

1. Under the registry key `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio` the DWORD value **`DisableProtectedAudioDG`** has to be set to `1`. This disables the signature check for APOs, so that unsigned APOs will be loaded. This also means that applications requiring a secure audio path may change their behaviour or refuse to output audio altogether.
2. The APO COM class has to be registered under `HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\<GUID>`, where GUID is the GUID value identifying the APO. A class name has to be set as the default value of the GUID key. Inside the GUID key, the key `InprocServer32` has to be created, whose default value has to be set to the path to the DLL file. Also, a value `ThreadingModel` has to be set to an appropriate value.
3. The key `HKEY_LOCAL_MACHINE\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\<GUID>` has to be created, which is normally handled by the function `RegisterAPO` (declared in `audioenginebaseapo.h` in the Windows DDK). The corresponding function `UnregisterAPO` can be used to remove the key.
4. The APO has to be registered for a specific device under `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\<endpoint GUID>\FxProperties`. The **Render** path contains output devices while the **Capture** path contains input devices. In the `FxProperties` key:
   - **`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1`** defines the GUID of an **LFX** APO
   - **`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2`** defines the GUID of a **GFX** APO

   Normally, these values already exist and refer to the audio driver's APOs. To register the custom APO, one of the values has to be replaced, so **the existing values have to be saved somewhere else (Equalizer APO saves them in `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO\Child APOs`)**. They are needed to restore the original values when uninstalling the custom APO and, as the custom APO is meant to be used in addition to the existing APOs, it has to load and call the original APO so that it can continue to perform its function.

   Since **Windows 8.1**:
   - **`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5`** and **`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6`** are also used for **LFX** and **GFX**, respectively. When an APO is registered via the new values (ending in 5 or 6), any APO registered via an old value (ending in 1 or 2) is **ignored**.
   - **`{d3993a3f-99c2-4402-b5ec-a92a0367664b},5`** and **`{d3993a3f-99c2-4402-b5ec-a92a0367664b},6`** seem to specify a set of processing modes (type `MULTI_SZ`, so can contain multiple lines). Both of these normally need to be set to **`{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}`**, which is the default processing mode.

### Example project

A minimal example project for Visual Studio 2013 is attached to the original wiki page. It contains only the absolute necessary and has no dependencies that are not already included with Visual Studio 2013.

---

## Implications for MicGain (maintainer notes, not part of the original reference)

1. **`FxProperties` value names now confirmed [DOC]**: LFX/GFX = `{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1`/`,2` (legacy), `,5`/`,6` (Win 8.1+), plus processing-mode values `{d3993a3f-99c2-4402-b5ec-a92a0367664b},5`/`,6` set to `{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}`. Upgrades `docs/research-notes.md` §5 from [COMMUNITY] to [DOC]. Still capture a real `.reg` diff on a VM to confirm the current APO version's exact behavior (incl. SFX/MFX/EFX variants if used).
2. **Backup location confirmed [DOC]**: Equalizer APO saves replaced child APO GUIDs in `HKLM\SOFTWARE\EqualizerAPO\Child APOs`. Any registry-write enablement we implement (T2.2 approach B) MUST write this same backup key so APO's own uninstaller can still restore vendor APOs — and our rollback doc gets a concrete restore path.
3. **Per-device detection heuristic**: a device has Equalizer APO enabled iff its `FxProperties` LFX/GFX (or `,5`/`,6`) value contains the EqualizerAPO COM CLSID. The CLSID itself can be read from `HKLM\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\` or resolved via `EqualizerAPO.dll`'s registration — exact GUID value: `NEEDS-VM-VERIFICATION` (read from a real install or the Configurator source).
4. **`DisableProtectedAudioDG=1` is global and security-relevant**: it disables APO signature checks system-wide and may affect DRM/secure-path apps. Our consent dialog (T2.1/T2.2) must mention this side effect explicitly — it is part of "informed consent".
5. **Win 8.1+ precedence rule matters for detection**: if `,5`/`,6` values exist, `,1`/`,2` are ignored — detection logic must check the new values first to avoid false positives from stale legacy entries.
6. **Input devices only support LFX** (one LFX per capture device, no GFX) — relevant for mic enablement: only one registration point to check/write on the Capture side.
7. We never build Equalizer APO ourselves — compilation prerequisites are kept for completeness and for reading the Configurator source (T0.2 `NEEDS-SOURCE-VERIFICATION` items).
