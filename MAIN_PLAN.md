Executive summary
=================

Build a small Windows desktop utility ("MicGain MVP") in **C# / .NET 8 + WPF**, shipped as a self-contained single-file `.exe`. The app detects Equalizer APO, offers a consented install onto the default output device if missing, and — when APO is present — shows a single window with one grouped device dropdown and one gain slider that writes `Preamp:` lines into Equalizer APO's per-device config files. The riskiest area is **automating APO installation per-device**: Equalizer APO's Configurator has no documented silent CLI, so device enablement means replicating its registry writes (feasible, but needs verification and a rollback path). The MVP is structured as one vertical slice first (detect → read config → slider changes gain on one already-APO-enabled device), then install automation second.

Feasibility notes
=================

**Feasible with standard mechanisms:**

*   **Detection**: Check for `HKLM\SOFTWARE\EqualizerAPO` / uninstall registry key and/or `C:\Program Files\EqualizerAPO\` presence. _(Assumption: registry key path — needs verification on a real install.)_
*   **Device enumeration**: Windows Core Audio (`IMMDeviceEnumerator`) gives render/capture devices, default device, and device GUIDs. Mature, stable API.
*   **Gain control**: Equalizer APO hot-reloads its text config (`config\config.txt` and includes). Writing `Device:` blocks with `Preamp: X dB` (or per-device include files) is the documented, supported mechanism. No APO internals needed for this part.
*   **Per-device APO status**: Which devices have APO installed is stored in `HKLM\...\MMDevices\Audio\{Render|Capture}\{device-guid}\FxProperties` (APO CLSID entries written by Configurator). Readable without admin. _(Needs verification: exact property keys per APO version and Windows version.)_

**Hard / partially infeasible — called out explicitly:**

1.  **Silent install of Equalizer APO itself**: the installer (NSIS) likely supports `/S`, but the agent environment has no internet — so the installer must be **bundled with our exe** or the user must point to a downloaded copy. _(Assumption: `/S` works; verify. Licensing of redistribution: GPLv2 — bundling is permissible but must be confirmed and attributed.)_
2.  **Silent device selection**: `Configurator.exe` has **no documented CLI for unattended device enablement**. Closest workable alternative: write the `FxProperties` registry values ourselves (replicating what Configurator does), which requires admin and is version-sensitive. Fallback for MVP: launch Configurator interactively with clear user guidance — lower risk, slightly more friction.
3.  **Reboot/audio-service restart**: APO enablement typically requires restarting the Windows Audio service or rebooting; the affected device's audio is briefly interrupted. Must be consented and messaged, never automatic-silent.
4.  **Admin/UAC**: Install flow needs elevation (HKLM writes, Program Files). Slider flow may not — _(unknown: whether the APO installer ACLs the `config` folder for user writes; verify; if not, we need an elevated write path or run elevated throughout for MVP)._

**Why not WinUI 3**: WinUI 3 adds the Windows App SDK runtime dependency (or large self-contained bundles), heavier packaging, and a younger toolchain — all friction for a single-window utility that must become one simple exe. WPF on .NET 8 is mature, packages to a single self-contained exe trivially, and has zero UI-framework runtime prerequisites. WinForms would also work; WPF chosen for slightly better data-binding for dropdown/slider. This is the justified deviation from WinUI 3.

Recommended stack
=================

Concern

Choice

Rationale

Language/runtime

C# / .NET 8

Single-file self-contained publish; first-class Windows interop

UI

WPF (single window, MVVM-lite)

Mature, zero extra runtime, trivial packaging

Audio device enumeration

NAudio (`MMDeviceEnumerator` wrapper) **or** direct COM interop

One small, well-known dependency; drop to raw interop if we want zero deps

Registry / config IO

BCL (`Microsoft.Win32`, `System.IO`)

No deps

Elevation

App manifest `asInvoker` + relaunch-elevated helper for install flow

Least-privilege; slider works unelevated if config dir is user-writable

Packaging

`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`

One exe, no installer needed for MVP

APO installer

Bundled `EqualizerAPO64-x.x.x.exe` as embedded resource / sibling file

No internet assumption at runtime

Proposed architecture
=====================

Copy to clipboard

Insert at cursor

    ┌─────────────────────────────────────────────┐
    │ WPF Shell (MainWindow)                      │
    │  - Install prompt dialog                    │
    │  - Device dropdown (grouped In/Out)         │
    │  - Gain slider                              │
    └──────────────┬──────────────────────────────┘
                   │ ViewModel (state machine:
                   │  NotInstalled / Installing / Ready / NoDevice / Error)
    ┌──────────────┴──────────────────────────────┐
    │ Core services (pure C#, unit-testable)      │
    │  1. ApoDetectionService  → registry + FS    │
    │  2. AudioDeviceService   → Core Audio enum, │
    │       default device, APO-enabled filter    │
    │  3. ApoConfigService     → read/write       │
    │       per-device gain in APO config files   │
    │  4. ApoInstallService    → bundled installer│
    │       /S + device enablement + rollback     │
    └──────────────┬──────────────────────────────┘
                   │
       Windows: Registry (HKLM MMDevices FxProperties),
       FS (Program Files\EqualizerAPO\config\*.txt),
       Core Audio COM, Windows Audio service restart
    

**Data flow (steady state):** App start → detect APO → enumerate devices → filter to APO-enabled → populate dropdown → on selection, `ApoConfigService` reads current `Preamp:` for that device → slider reflects it → slider change (debounced ~150 ms) → write config → APO hot-reloads → gain applied.

**Config storage strategy:** One include file per managed device, e.g. `config\micgain\{deviceGuid}.txt` containing only `Preamp: X dB`, referenced from `config.txt` via `Device:`\-scoped `Include:` blocks inside a clearly marked `# BEGIN micgain / # END micgain` region. We never rewrite user content outside our markers — non-destructive by design.

Repo structure
==============

Copy to clipboard

Insert at cursor

    /src
      /MicGain.App            # WPF shell, ViewModels, dialogs
      /MicGain.Core           # the 4 services, models, state machine (no UI refs)
      /MicGain.Elevated       # tiny console helper for elevated install steps (optional, Phase 2)
    /assets
      /installer              # bundled EqualizerAPO installer + license/attribution
    /tests
      /MicGain.Core.Tests     # unit tests (config parsing/writing, detection logic)
    /docs
      research-notes.md       # registry keys, installer flags — verified findings
      architecture.md
    README.md
    

Phased plan
===========

Phase 0 — Research spikes (verification, no product code)
---------------------------------------------------------

Verify the assumptions everything else depends on: registry keys for detection and per-device APO status, installer `/S` behavior, config-folder write permissions, hot-reload behavior, `Device:` matching syntax.

Phase 1 — Vertical slice: gain control on an already-enabled device
-------------------------------------------------------------------

Assumes APO is installed manually (by the developer/tester). Detect APO → enumerate APO-enabled devices → dropdown + slider → write config → audible gain change. **This proves the product end to end** minus install automation.

Phase 2 — Install flow
----------------------

Detection of "not installed" → consent prompt → default output device detection → bundled silent install → device enablement (registry write **or** guided Configurator launch as fallback) → audio service restart prompt → graceful exits on decline / no device.

Phase 3 — Hardening & packaging
-------------------------------

UAC strategy finalized, rollback/uninstall notes, error states, single-file publish, smoke-test checklist.

Task breakdown
==============

* * *

**T0.1 — Spike: APO detection & per-device status registry map**

*   **Goal**: Document exact registry keys/values for (a) APO installed, (b) APO enabled on a given render/capture device, across at least Win10/Win11 + current APO version.
*   **Inputs**: Test VM with Equalizer APO installed via Configurator on one output + one input device.
*   **Outputs**: `docs/research-notes.md` section with key paths, value names, sample data.
*   **Dependencies**: none.
*   **Acceptance criteria**: A reviewer can, from the doc alone, hand-check on a machine whether APO is installed globally and on a specific device.
*   **Risks**: Keys differ across APO versions/Windows builds; mitigate by recording version matrix.

* * *

**T0.2 — Spike: installer silent mode + config folder permissions + hot reload**

*   **Goal**: Confirm `/S` (or equivalent) silent install behavior, whether `config\` is writable by non-admin users post-install, and that `Preamp:` edits apply live without service restart.
*   **Inputs**: Bundled installer binary; test VM.
*   **Outputs**: Research notes incl. exact `Device:` matching syntax that reliably targets one device.
*   **Dependencies**: none (parallel with T0.1).
*   **Acceptance criteria**: Documented yes/no for each question with reproduction steps; recommended elevation strategy stated.
*   **Risks**: `/S` may still pop UAC or require reboot; config may need admin writes → drives MVP elevation design.

* * *

**T1.1 — Core: ApoDetectionService + AudioDeviceService**

*   **Goal**: Detect APO installation; enumerate devices grouped render/capture with friendly names, GUIDs, default-device flag, and per-device APO-enabled flag.
*   **Inputs**: T0.1 findings.
*   **Outputs**: `MicGain.Core` services + models + unit tests (registry/FS behind interfaces for testability).
*   **Dependencies**: T0.1.
*   **Acceptance criteria**: On a machine with APO on 2 devices, service returns exactly those 2 flagged; on a clean machine, detection returns NotInstalled; unit tests pass with mocked registry/FS.
*   **Risks**: Device GUID format mismatch between Core Audio ID strings and registry key names (needs normalization).

* * *

**T1.2 — Core: ApoConfigService (read/write gain, non-destructive)**

*   **Goal**: Read current gain and write `Preamp: X dB` for a target device via marker-delimited include files; never touch content outside markers; create includes idempotently.
*   **Inputs**: T0.2 syntax findings.
*   **Outputs**: Service + unit tests covering: fresh config, existing user config, our markers present, malformed file (fail safe, no write).
*   **Dependencies**: T0.2.
*   **Acceptance criteria**: Round-trip test (write −12 dB, re-read −12 dB); diff of `config.txt` before/after shows only marker-region changes; malformed input never corrupts the file.
*   **Risks**: `Device:` selector ambiguity when two devices share names; mitigate by using GUID-based selectors if supported (verify in T0.2).

* * *

**T1.3 — UI: Main window (dropdown + slider) wired to Core**

*   **Goal**: Single window: grouped dropdown of APO-enabled devices (Input/Output headers), one slider (suggest −30…+15 dB, default 0), debounced writes, slider reflects stored value on selection change.
*   **Inputs**: T1.1, T1.2.
*   **Outputs**: Working app for the steady-state path; "Ready" and "no APO-enabled devices" states.
*   **Dependencies**: T1.1, T1.2.
*   **Acceptance criteria**: Manual test — moving slider on an APO-enabled mic audibly changes recorded level within ~1 s; switching devices loads each device's own stored gain; app never writes config without user slider interaction.
*   **Risks**: Hot-reload latency varies; debounce tuning.

* * *

**T2.1 — Install consent flow + default device detection**

*   **Goal**: On APO-not-installed: show consent dialog ("Install Equalizer APO on ?"); decline → exit gracefully; no active output device → clear message → exit gracefully.
*   **Inputs**: T1.1.
*   **Outputs**: State-machine flow + dialogs; no installation logic yet (stub).
*   **Dependencies**: T1.1.
*   **Acceptance criteria**: With APO absent: decline path exits cleanly with no system changes; with all output devices disabled: correct message and clean exit; consent dialog names the actual default device.
*   **Risks**: "No active output device" detection edge cases (disabled vs. unplugged states).

* * *

**T2.2 — ApoInstallService: bundled silent install + device enablement + rollback**

*   **Goal**: Run bundled installer silently (elevated), enable APO on the consented device (registry write per T0.1, **fallback**: launch Configurator with on-screen guidance if registry approach proves too fragile), prompt for audio-service restart with consent, record a rollback note (what was written, how to undo / uninstall path).
*   **Inputs**: T0.1, T0.2, T2.1.
*   **Outputs**: Install service + elevation path + `docs/rollback.md`.
*   **Dependencies**: T0.1, T0.2, T2.1.
*   **Acceptance criteria**: On a clean VM: accept → APO installed and enabled on default output device → app transitions to Ready state (possibly after consented restart); every system change is preceded by explicit consent; uninstalling APO via its own uninstaller restores a clean state.
*   **Risks**: **Highest-risk task.** Registry write approach may break on APO updates; UAC prompt unavoidable; service restart interrupts audio. Fallback to guided Configurator launch is the explicit de-risking path.

* * *

**T3.1 — Packaging + smoke checklist**

*   **Goal**: Single-file self-contained exe; app manifest finalized (elevation strategy from T0.2); README run instructions; manual smoke-test checklist.
*   **Inputs**: All prior tasks.
*   **Outputs**: Published exe artifact; `docs/smoke-checklist.md`.
*   **Dependencies**: T1.3, T2.2.
*   **Acceptance criteria**: Fresh Windows VM: copy exe → full flow (install consent → enable → adjust mic gain) works end to end following only the README.
*   **Risks**: SmartScreen warnings on unsigned exe (accepted for MVP; note code-signing as future work).

Test strategy
=============

*   **Unit tests** (CI-friendly, no Windows audio needed): config file parser/writer (the most corruption-sensitive code — highest coverage here), detection logic, device-list filtering, state machine transitions — all with mocked registry/FS/audio interfaces.
*   **Manual integration matrix** (VM-based, scripted checklist): clean machine / APO installed / APO installed on zero devices / no active output device / decline-install path / two devices with identical names / capture-only device.
*   **Non-destructiveness check**: snapshot `config.txt` + relevant registry keys before each manual run; diff afterward; only marker-region and consented changes allowed.
*   **Soak check (lightweight)**: rapid slider movement for 60 s → no config corruption, no APO crash, final value persisted.
*   No automated UI tests for MVP — single window, manual checklist is cheaper and sufficient.

Open questions / unknowns
=========================

1.  **(Verify, T0.1)** Exact registry layout Configurator writes per device (`FxProperties` GUID/value names) and its stability across APO versions.
2.  **(Verify, T0.2)** Installer silent flag behavior; whether it triggers reboot requirement.
3.  **(Verify, T0.2)** Is `Program Files\EqualizerAPO\config\` user-writable post-install? Determines whether the slider path needs elevation.
4.  **(Verify, T0.2)** Most reliable `Device:` selector syntax to target one specific device (name vs. GUID fragments), and behavior with duplicate device names.
5.  **(Assumption)** Hot reload applies `Preamp:` changes within ~1 s without service restart — believed true, must confirm on capture devices specifically.
6.  **(Decision pending T0.1/T2.2)** Registry-write device enablement vs. guided Configurator launch — choose after spike results.
7.  **(Legal)** Equalizer APO is GPLv2; confirm bundling/redistribution approach and required attribution (our app stays separate-process, so no license contamination expected — verify).
8.  **(Assumption)** "Mic gain" via `Preamp:` on a capture device is the desired semantic (post-capture DSP gain, distinct from the Windows mic level slider). Flag to stakeholder.

Suggested
=========

Start with **T0.1 and T0.2 in parallel** on a disposable Windows VM, writing findings into `docs/research-notes.md`. These two spikes resolve every blocking unknown (detection keys, silent install, config permissions, device-selector syntax) and determine the elevation strategy and the install-automation approach before any product code is written. Each subsequent task (T1.1 → T3.1) maps cleanly to one branch / one merge request.