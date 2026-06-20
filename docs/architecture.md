# Architecture — MicGain MVP

> Component overview and data flow. Concrete — every class, interface, and path
> mentioned here exists in the repo. Cross-reference with `MAIN_PLAN.md` and
> `AGENTS.md` for design rationale and constraints.

## Layer diagram

```
┌─────────────────────────────────────────────────────────────┐
│ WPF Shell  (src/MicGain.App)                                │
│                                                             │
│  App.xaml.cs          ── composition root, startup branching │
│  MainWindow.xaml.cs   ── single-window host                  │
│  ViewModels/                                                │
│    MainViewModel      ── device list + Refresh               │
│    DeviceGainViewModel ── one slider row per device          │
│    InstallConsentViewModel ── consent dialog state           │
│    RelayCommand / ViewModelBase ── MVVM helpers              │
│  Views/                                                     │
│    InstallConsentDialog ── consent UI + WpfInstallInteraction│
│    WpfInstallInteraction ── IInstallInteraction impl         │
└──────────────┬──────────────────────────────────────────────┘
               │  depends on MicGain.Core (project reference)
┌──────────────┴──────────────────────────────────────────────┐
│ Core services  (src/MicGain.Core)                            │
│                                                              │
│  Services/                                                   │
│    ApoDetectionService  (IApoDetectionService)               │
│      → reads HKLM\SOFTWARE\EqualizerAPO\ConfigPath           │
│      → sanity-checks config dir exists on disk               │
│    AudioDeviceService   (IAudioDeviceService)                │
│      → enumerates capture devices via NAudio/COM             │
│      → reads per-device FxProperties for APO-enabled flag    │
│    ApoConfigService     (IApoConfigService)                  │
│      → reads/writes Preamp: inside # BEGIN micgain / END     │
│      → creates per-device include files in micgain\ folder   │
│    ApoInstallService    (IApoInstallService)                 │
│      → primary: guided installer + Configurator              │
│      → advanced: direct FxProperties registry writes         │
│      → rollback journal for the advanced path                │
│    InstallConsentStateMachine ── state model for T2.1/T2.2   │
│                                                              │
│  Models/                                                     │
│    AudioDeviceInfo, ApoDetectionResult, DeviceFlow,          │
│    GainRange, InstallFlowState, InstallOutcome, AudioEndpoint│
│                                                              │
│  IO/  (abstractions — AGENTS.md rule 3)                      │
│    IFileSystem / IRegistryReader / IRegistryWriter           │
│    IProcessRunner / IAudioDeviceEnumerator                   │
│    IInstallInteraction                                       │
│                                                              │
│  Audio/                                                      │
│    NAudioDeviceEnumerator ── wraps MMDeviceEnumerator        │
│    CoreAudioEndpointId ── GUID extraction from endpoint IDs  │
└──────────────┬──────────────────────────────────────────────┘
               │
    Windows: Registry (HKLM), FileSystem (config\*.txt),
    Core Audio COM, net.exe (audiosrv restart)
```

## Startup flow (App.xaml.cs)

```
OnStartup
  │
  ├─ Detect APO
  │    ├─ registry HKLM\SOFTWARE\EqualizerAPO\ConfigPath
  │    └─ filesystem sanity check (config dir exists)
  │
  ├─ APO not installed ── RunInstallConsentFlow()
  │    │
  │    ├─ InstallConsentViewModel detects default output device
  │    │    ├─ NoDevice → show message → Shutdown()
  │    │    └─ HasDevice → show InstallConsentDialog
  │    │
  │    ├─ User declines → Shutdown() (zero system changes)
  │    │
  │    └─ User consents → ApoInstallService.RunGuidedInstallAsync()
  │         │
  │         ├─ Consent: run installer (UAC)
  │         │    └─ InstallerNotFound → message → Shutdown()
  │         ├─ Wait for Configurator (user selects device)
  │         │    └─ DeviceNotEnabled → message → Shutdown()
  │         └─ Consent: restart audiosrv
  │              ├─ Accepted → Succeeded → message → ShowMainWindow()
  │              └─ Declined → SucceededPendingRestart → message → ShowMainWindow()
  │
  └─ APO installed ── ShowMainWindow()
       │
       └─ MainViewModel.LoadAsync()
            ├─ ApoDetectionService.Detect() → config path
            ├─ AudioDeviceService.GetDevices() → capture devices + IsApoEnabled flag
            ├─ ApoConfigService.ReadGain() per device → initial slider values
            └─ State = Ready (or NoCaptureDevices / ApoNotInstalled / Error)
```

## Device enumeration & APO-enabled flag

`AudioDeviceService` (implements `IAudioDeviceService`):

1. `IMMDeviceEnumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE)` → live device list.
   Registry `DeviceState` is stale (VM-verified, `research-notes.md` §10); the COM API is
   authoritative.
2. For each device: extract endpoint GUID from `IMMDevice::GetId()`
   (`CoreAudioEndpointId` strips the `{0.0.1.00000000}.` prefix).
3. Read `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{guid}\FxProperties`.
4. Device is **APO-enabled** iff:
   - `{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5` = Equalizer APO LFX CLSID
     `{EACD2258-FCAC-4FF4-B36D-419E924A6D79}` (Win 8.1+, checked first per the
     documented precedence rule), **OR**
   - `,1` = same CLSID (legacy fallback).
5. Friendly name from `PKEY_Device_FriendlyName`.

## Config file layout

All writes are scoped inside marker regions in `config.txt`:

```
# BEGIN micgain
Device: {endpoint-guid-1}
Include: micgain\{endpoint-guid-1}.txt
Device: {endpoint-guid-2}
Include: micgain\{endpoint-guid-2}.txt
Device: all
# END micgain
```

Each include file (`micgain\{guid}.txt`) contains:

```
Preamp: X dB
```

The `Device: all` line before `# END micgain` is mandatory: it resets device scope.
Without it, user content below the markers would inherit a stale device scope
(VM-verified scope-leak rule, `apo-config-reference.md` maintainer note 2).

**Key guarantees** (`ApoConfigService`):

| Operation | Behavior |
|---|---|
| `ReadGain(guid)` | Parses the include file; returns `null` if the device isn't managed yet (lenient). |
| `WriteGain(guid, dB)` | Creates the `micgain\` directory and include file if absent; inserts the `# BEGIN micgain` marker block into `config.txt` if absent; updates only `Preamp:` inside the include file. |
| Malformed config | Any exception during read/write → fail safe, no write, error surfaced to the status line. |
| Concurrent writes | Serialized via `SemaphoreSlim` owned by `MainViewModel`, shared across all `DeviceGainViewModel` instances. |

## Gain slider debounce

`DeviceGainViewModel`:

- Slider `Value` two-way-bound to `GainDb`.
- Each change schedules a debounced write (~150 ms).
- New slider movement cancels the pending debounce (only the final position is persisted).
- Writes are serialized through the shared `SemaphoreSlim` — only one `WriteGain` call
  can be in flight at a time (because `config.txt` itself may be rewritten).

## Install flow state machine

`InstallConsentStateMachine` governs the T2.1/T2.2 states:

```
NotInstalled ── Initialize() ──→ HasDevice
                                │
                    ┌───────────┘
                    ▼
              HasDevice ── User declines ──→ Declined (→ Shutdown)
                    │
                    └── User consents ──→ Installing
                                            │
                        ┌───────────────────┼───────────────────┐
                        ▼                   ▼                   ▼
                   CompleteInstall()   FailInstall()        FailInstall()
                   Succeeded /             │                   │
                   SucceededPendingRestart │                   │
                        │              ConsentDeclined    DeviceNotEnabled /
                        │              InstallerNotFound  FailedRolledBack
                        ▼
                      Ready
```

## IO abstractions (testability)

All platform boundaries are behind interfaces in `MicGain.Core/IO/`:

| Interface | Real impl (prod) | Fake (tests) |
|---|---|---|
| `IRegistryReader` | `WindowsRegistryReader` | `FakeRegistryReader` |
| `IRegistryWriter` | `WindowsRegistryWriter` | `FakeRegistryWriter` |
| `IFileSystem` | `PhysicalFileSystem` | `FakeFileSystem` |
| `IProcessRunner` | `SystemProcessRunner` | `FakeProcessRunner` |
| `IAudioDeviceEnumerator` | `NAudioDeviceEnumerator` | `FakeAudioDeviceEnumerator` |
| `IInstallInteraction` | `WpfInstallInteraction` | `FakeInstallInteraction` |

Unit tests in `tests/` never touch real Windows APIs — CI-safe everywhere.

## Elevation model

| Path | Elevation | When |
|---|---|---|
| App shell (`asInvoker`) | None | Always |
| Installer execution | Elevated (UAC via `ProcessRunner.RunElevatedAsync`) | During T2.2 guided install |
| Registry FxProperties writes | Elevated (same mechanism) | Advanced path only |
| Audio service restart (`net.exe`) | Elevated | After install/enablement |
| Config file reads/writes | None (Users group has write on `config\`) | Steady state |

Per AGENTS.md rule 4: the app manifest declares `asInvoker`; every elevated action
is a separate `RunElevatedAsync` call that triggers its own UAC prompt.

## Non-destructiveness enforcement

1. All config writes by `ApoConfigService` land inside `# BEGIN micgain` / `# END micgain`
   markers in `config.txt`.
2. Per-device values are in separate `micgain\{guid}.txt` files — one `Preamp:` line each.
3. `WriteGain` opens `config.txt`, identifies the marker block; if absent, appends it at EOF;
   if present, rewrites only the block. Everything outside the block is left verbatim.
4. `ApoInstallService` (advanced path) journals every registry write and rolls back on
   failure. The primary (guided) path makes no MicGain-owned registry writes — Equalizer
   APO's own installer and Configurator handle their own backup/restore via `Child APOs`.

## Key constants (research-notes §11, VM-verified)

| Constant | Value | Source |
|---|---|---|
| Equalizer APO LFX CLSID | `{EACD2258-FCAC-4FF4-B36D-419E924A6D79}` | `FxProperties` `,5` |
| Equalizer APO GFX CLSID | `{637c490d-eee3-4c0a-973f-371958802da2}` | `FxProperties` `,6` |
| FxProperties property key | `{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}` | dev-ref §Registry changes item 4 |
| Processing-mode property key | `{d3993a3f-99c2-4402-b5ec-a92a0367664b}` | dev-ref §Registry changes item 4 |
| Default processing mode | `{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}` | dev-ref §Registry changes item 4 |
| Child APOs backup key | `HKLM\SOFTWARE\EqualizerAPO\Child APOs\{guid}` | VM-verified, research-notes §11 |
| DisableProtectedAudioDG | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio` / `1` | dev-ref item 1 |
| Gain range | −30 dB to +15 dB | `GainRange` in `MicGain.Core/Models/` |