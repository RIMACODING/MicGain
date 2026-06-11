# Research notes — Equalizer APO (T0.2 spike)

> **Status**: Desk research, **re-graded 2026-06-10 against the canonical internal references** that now ship in this repo (issue #2):
> - `docs/internal/apo-config-reference.md` — cited below as **config-ref**
> - `docs/internal/apo-development-reference.md` — cited below as **dev-ref**
> - `docs/internal/apo-install-troubleshooting-reference.md` — cited below as **install-ref**
>
> Confidence labels:
> - **[DOC]** — directly backed by a canonical internal reference (citation given)
> - **[COMMUNITY]** — widely reported by users/forums, believed reliable; not confirmed by canonical docs
> - **[ASSUMPTION]** — inferred, must be confirmed
>
> Per AGENTS.md, findings are only upgraded where a canonical doc **directly** backs them. Anything that can only be confirmed on a real Windows machine with Equalizer APO (registry capture, silent-install behavior, hot-reload latency, audible checks) keeps an explicit `NEEDS-VM-VERIFICATION` flag regardless of how plausible it is. `NEEDS-SOURCE-VERIFICATION` = confirm by reading the Equalizer APO source tree on SourceForge (https://sourceforge.net/projects/equalizerapo/).

---

## 1. Global install detection

| Finding | Confidence | Verification |
|---|---|---|
| Equalizer APO's registry home is `HKLM\SOFTWARE\EqualizerAPO`; it holds a `ConfigPath` string value pointing to the config directory. | **[DOC]** (config-ref §Expression commands — the official `readRegString` example reads `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO`, value `ConfigPath`; install-ref §Log files places `EnableTrace` under the same key) | Exact value data on a real install (default `C:\Program Files\EqualizerAPO\config`): `NEEDS-VM-VERIFICATION` (low risk) |
| The installer is NSIS-based. | **[DOC]** (dev-ref §Compilation prerequisites — NSIS plus NSISpcre, AccessControl, nsArray plugins are required to build the installer) | — |
| Standard NSIS uninstall entry exists under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EqualizerAPO` (or similar key name) with `UninstallString`. | [ASSUMPTION] — not covered by canonical docs | `NEEDS-VM-VERIFICATION` |
| Install directory default `C:\Program Files\EqualizerAPO\` containing `Configurator.exe`, `Editor.exe` (Configuration Editor), `config\config.txt`. Custom install paths are explicitly supported. | **[DOC]** (install-ref §Installation tutorial steps 2–3, §Configuration tutorial step 1) | Confirm on VM anyway (low risk) |

**Detection strategy recommendation (unchanged, now [DOC]-backed)**: primary = `HKLM\SOFTWARE\EqualizerAPO\ConfigPath` (also yields the config dir); secondary sanity check = config dir exists on disk. Do **not** hardcode `Program Files` paths — install-ref explicitly allows non-default paths; always resolve via the registry value.

## 2. `config.txt` syntax (gain-control path)

| Finding | Confidence | Verification |
|---|---|---|
| APO hot-reloads config files on save — no service restart needed for config changes. | **[DOC]** (install-ref §Configuration tutorial step 2: "the volume changes immediately each time after you save the file"; config-ref §Convolution documents automatic reload when files in the config path change) | Latency bound (~1 s) and capture-device behavior: `NEEDS-VM-VERIFICATION` |
| `Preamp: <value> dB` sets gain; e.g. `Preamp: -6.5 dB`. Since v0.8, multiple preamps applying to the same channel **sum in dB**. | **[DOC]** (config-ref §Preamp) | Syntax is documented as `<Negative number> dB`; **positive values** are widely used but not literally documented, and accepted decimal/locale format is unstated → `NEEDS-VM-VERIFICATION`; cap the UI slider's positive range conservatively |
| `Device: <pattern>` scopes all following commands (until the next `Device:` line) to matching devices. Patterns are space-separated words that must **all** be found in the string `"Device_name Connection_name GUID"`; alternatives separated by `;`; keyword `all` matches everything. | **[DOC]** (config-ref §Device) | Case sensitivity is unstated in the doc → `NEEDS-VM-VERIFICATION` |
| Because the device GUID participates in matching, **using the endpoint GUID as the `Device:` pattern uniquely targets one device**, immune to duplicate friendly names. | **[DOC]** (config-ref §Device — GUID is part of the matched string) | Linchpin of the per-device design → still run the on-machine acceptance test: `NEEDS-VM-VERIFICATION` |
| **Scope-leak rule (was an open point, now resolved in design)**: a `Device:` line that does not match causes **all following commands except `Device:` commands to be ignored** — i.e. device scope persists until the next `Device:` line. Our marker region MUST therefore end with `Device: all` before `# END micgain` so user content below the markers is never left in our device scope. | **[DOC]** (config-ref §Device; config-ref maintainer note 2) | Runtime double-check with user content after our region: `NEEDS-VM-VERIFICATION` |
| `Include: <path>` loads another config file. | **[DOC]** (config-ref §Include) | **Relative-path resolution is documented only for `Convolution:`** ("relative to the current configuration file's path"), not for `Include:` — per the no-guessing rule, Include's resolution base is `NEEDS-VM-VERIFICATION` |
| Capture (microphone) devices are processed on the `capture` stage, which is selected by default, so plain `Preamp:` lines apply to input devices with no extra `Stage:` command. | **[DOC]** (config-ref §Stage: "For input devices, there is only the capture stage… initially, the selected stages are post-mix and capture") | Audible check on a real mic: `NEEDS-VM-VERIFICATION` |
| **Parser leniency**: lines not matching `Command: Parameters`, and unknown command names, are **silently ignored** by APO. | **[DOC]** (config-ref §General format) | — informs our fail-safe reader; our writer only emits well-formed lines |
| **Writer constraints**: inline expressions are delimited by backticks and are forbidden in `Device:`/`If:` lines; `If:` cannot gate `Device:` statements (Device has higher priority). Our generated region must use plain literal lines and must never emit backticks. | **[DOC]** (config-ref §Eval and inline expressions, §If/ElseIf/Else/EndIf; maintainer notes 8–9) | — |

**Planned config layout** (updated with the `Device: all` reset, per the scope-leak rule above):

```
# BEGIN micgain
Device: {endpoint-guid-1}
Include: micgain\{endpoint-guid-1}.txt
Device: {endpoint-guid-2}
Include: micgain\{endpoint-guid-2}.txt
Device: all
# END micgain
```

Each include file contains a single `Preamp: X dB` line. **Fresh-install fixture [DOC]** (install-ref §Configuration tutorial): default `config.txt` is a `Preamp:` line plus `Include: example.txt` — tests must use this exact shape as the "fresh config" case and the writer must preserve it untouched outside our markers.

## 3. Config directory permissions / elevation strategy

| Finding | Confidence | Verification |
|---|---|---|
| The installer grants the local **Users** group write/modify permission on the `config` directory so that the Configuration Editor (running unelevated) can save configs. | [COMMUNITY] — not directly stated in canonical docs. Corroborating (but **not sufficient**) signal: dev-ref §Compilation prerequisites lists the NSIS **AccessControl** plugin as required to build the installer, implying the installer sets ACLs somewhere. | `NEEDS-VM-VERIFICATION` — check ACLs post-install (`icacls`) |

**Implication if true (unchanged)**: the steady-state slider path runs fully **unelevated** (`asInvoker`), satisfying AGENTS.md rule 4. Only the install/enable flow needs elevation. If false: fall back to an elevated write helper (`MicGain.Elevated`).

## 4. Installer silent mode

| Finding | Confidence | Verification |
|---|---|---|
| The installer is **NSIS-based**; NSIS installers conventionally accept `/S` (silent) and `/D=<dir>` (target dir). | NSIS base: **[DOC]** (dev-ref §Compilation prerequisites). `/S`/`/D` for *this specific installer*: [ASSUMPTION] (NSIS convention) | `NEEDS-VM-VERIFICATION` for this installer |
| The normal (interactive) install **launches `Configurator.exe` during setup** so the user selects devices. | **[DOC]** (install-ref §Installation tutorial step 3 — upgraded from [ASSUMPTION]) | Behavior of this step under `/S` (skipped? launched anyway?) is undocumented → `NEEDS-VM-VERIFICATION` — critical for the unattended flow |
| A **reboot is the documented recommendation** after install; the underlying requirement is that **the newly installed APO is only used after the Windows Audio service is restarted**. | **[DOC]** (install-ref §Installation tutorial step 4 — upgraded from [COMMUNITY]) | Whether restarting `audiosrv` alone (no reboot) reliably suffices in practice: `NEEDS-VM-VERIFICATION` |
| Redistribution: Equalizer APO is **GPLv2**. Bundling the unmodified installer alongside our app (separate process, no linking) does not contaminate our license, but we must ship attribution + the GPLv2 text and offer source availability per GPL §3. | [DOC] (license text) | Legal review recommended |

## 5. Per-device enablement — what Configurator does

| Finding | Confidence | Verification |
|---|---|---|
| Per-device APO registration lives under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{Render\|Capture}\{endpoint-guid}\FxProperties` (Render = output, Capture = input). Exact value names: **`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1`** = LFX APO GUID, **`,2`** = GFX APO GUID (legacy); since **Windows 8.1**, **`,5`** (LFX) and **`,6`** (GFX) are also used, and **when a `,5`/`,6` value exists, the corresponding `,1`/`,2` value is ignored**. Processing-mode values **`{d3993a3f-99c2-4402-b5ec-a92a0367664b},5`/`,6`** (MULTI_SZ) normally must be set to the default processing mode **`{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}`**. | **[DOC]** (dev-ref §Registry changes required to load the APO DLL, item 4 — upgraded from [COMMUNITY]; note the canonical doc uses LFX/GFX terminology, not SFX/MFX/EFX as previously written here) | Capture a real `.reg` before/after diff for one render + one capture device on Win10 + Win11 with the current APO version: `NEEDS-VM-VERIFICATION` |
| Configurator **backs up the pre-existing FxProperties values** so uninstall/deselect can restore the original (vendor) APOs. Backup location: **`HKLM\SOFTWARE\EqualizerAPO\Child APOs`**. Any registry-write reimplementation by us MUST replicate this backup or we break vendor audio enhancements irreversibly. | **[DOC]** (dev-ref §Registry changes item 4 — upgraded from [COMMUNITY]; backup key now named) | Exact value format inside `Child APOs`: `NEEDS-SOURCE-VERIFICATION` + `NEEDS-VM-VERIFICATION` |
| Loading unsigned APOs requires the DWORD **`DisableProtectedAudioDG = 1`** under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio`. This disables the APO signature check **system-wide** and may change behavior of apps requiring a secure audio path. Our consent dialog (T2.1/T2.2) must disclose this side effect explicitly. | **[DOC]** (dev-ref §Registry changes item 1) | — |
| The APO COM class is registered under `HKLM\SOFTWARE\Classes\CLSID\<GUID>` (+ `InprocServer32`) and `HKLM\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\<GUID>` (via `RegisterAPO`/`UnregisterAPO`). | **[DOC]** (dev-ref §Registry changes items 2–3) | Equalizer APO's concrete CLSID value: `NEEDS-VM-VERIFICATION` / `NEEDS-SOURCE-VERIFICATION` |
| **Detection heuristic**: a device has Equalizer APO enabled iff its `FxProperties` LFX/GFX value contains the EqualizerAPO CLSID — and detection must check `,5`/`,6` **first** (precedence rule) to avoid false positives/negatives from stale `,1`/`,2` entries. | **[DOC]** (derived strictly from dev-ref item 4 precedence rule) | End-to-end check against a real Configurator-enabled device: `NEEDS-VM-VERIFICATION` |
| **Input (capture) devices support only one LFX APO** — no GFX on capture. Only one registration point to check/write on the Capture side. | **[DOC]** (dev-ref §APO development) | — |
| Configurator offers per-device "troubleshooting options" (use/disable original APOs, install in pre-mix and/or post-mix stage only). Consequence for detection: enablement is **not strictly binary** — treat "Equalizer APO present in at least one stage" as enabled. Defaults are fine for MVP. | **[DOC]** (install-ref §Troubleshooting → Configurator) | Exact registry representation of each option: `NEEDS-VM-VERIFICATION` |
| Changes take effect after restarting the Windows Audio service; Configurator prompts for this. | **[DOC]** (install-ref §Installation tutorial step 4; dev-ref) | Low risk |
| Reading `FxProperties` does **not** require elevation; writing under HKLM does. | [DOC] (Windows ACL defaults) | Low risk |

### Endpoint GUID normalization
Core Audio `IMMDevice::GetId` returns IDs like `{0.0.0.00000000}.{guid}` (render) / `{0.0.1.00000000}.{guid}` (capture). The registry key under `MMDevices\Audio\{Render|Capture}\` is the bare `{guid}` part. `AudioDeviceService` must extract the trailing GUID for registry lookups and for `Device:` selectors. [DOC] — low risk, unit-testable.

## 6. Configurator.exe CLI

| Finding | Confidence | Verification |
|---|---|---|
| The NSIS uninstaller invokes Configurator with a command-line switch to deregister APOs from all devices, implying **at least an uninstall-mode CLI flag exists**. An install-mode flag may also exist. Exact flags, whether a *specific device* can be targeted unattended, and exit codes are unknown. | [ASSUMPTION] — canonical docs do not document any Configurator CLI | `NEEDS-SOURCE-VERIFICATION` (Configurator argument parsing in the SourceForge repo) + `NEEDS-VM-VERIFICATION` |
| No CLI for device selection is mentioned in the official documentation — install-ref describes only the interactive Configurator run (during setup and manually afterwards). If a CLI exists, it is undocumented and therefore version-fragile. | **[DOC]** (absence — install-ref §Installation tutorial step 3) | — |

## 7. Decision matrix — device enablement approach (for T2.2)

| Approach | Pros | Cons | Verdict |
|---|---|---|---|
| **A. Configurator CLI** | Uses APO's own code path incl. backup/restore; least likely to corrupt FxProperties | Undocumented; may not support targeting one device; version-fragile | Preferred **iff** source review confirms a usable flag |
| **B. Direct registry writes** | Full control, fully unattended. **Risk reduced since last revision**: dev-ref now documents the exact FxProperties value names, the `,5`/`,6` precedence rule, the processing-mode values, and the `Child APOs` backup key — so backup/restore parity has a concrete documented target. | Still version-sensitive across APO and Windows releases; corruption risk remains until verified against a real Configurator-produced `.reg` diff | Only with backup/restore parity (`Child APOs`) + version pinning + VM-verified `.reg` parity |
| **C. Guided interactive Configurator launch** | Zero fragility; APO handles everything | One manual step for the user (friction) | **Safe MVP fallback — acceptable** |

**Recommendation (unchanged)**: resolve A vs C after source review; B is the last resort, now with a documented spec to verify against. Elevation: install + enablement elevated (separate path), slider path unelevated (pending §3 confirmation).

## 8. Runtime failure modes & diagnostics (new — from install-ref)

| Finding | Confidence | Verification |
|---|---|---|
| **"Slider does nothing" failure state**: APOs can be disabled per device in Control Panel — Enhancements tab "Disable all enhancements" checked, or (if no Enhancements tab) Advanced tab "Enable audio enhancements" unchecked. The app's error/help state (T1.3/T3.1) should include this troubleshooting hint. | **[DOC]** (install-ref §Troubleshooting → Control Panel) | Feasibility of detecting this via registry: `NEEDS-VM-VERIFICATION` |
| Error log: `C:\Windows\ServiceProfiles\LocalService\AppData\Local\Temp\EqualizerAPO.log` — only created when an error occurs. Trace mode: set `EnableTrace` to `true` under `HKLM\SOFTWARE\EqualizerAPO` (set back to `false` afterwards). Useful for a future diagnostics feature and for VM verification sessions. | **[DOC]** (install-ref §Troubleshooting → Log files) | — |
| Hardware-accelerated OpenAL bypasses APOs entirely (vendor `*_oal.dll`); out of scope for MVP, note for support docs only. | **[DOC]** (install-ref §Troubleshooting → Hardware-accelerated OpenAL) | — |

## 10. T2.1 VM verification — device state detection (2026-06-11)

| Finding | Confidence | Verification |
|---|---|---|
| `Active` (state=1) and `Unplugged` (state=8) are distinct and reliably readable from `MMDevices\Audio\Render\{guid}` `DeviceState` registry value. | **[VM-VERIFIED]** | ✅ Confirmed on Win11 with Audient USB + Steam virtual speakers + Realtek HDA |
| `Disabled` (state=2) could not be triggered on the test VM — Audient virtual endpoints and Steam virtual speakers are protected and resist disabling via Sound settings. Registry did not update after `AudioSrv` restart or full reboot. | **[VM-VERIFIED — PARTIAL]** | Needs a machine with a standard disableable physical output (e.g. laptop built-in speakers). `NEEDS-VM-VERIFICATION` remains. |
| Registry `DeviceState` lags behind actual device state — does not update reliably without a full reboot. `AudioDeviceService` MUST use `IMMDeviceEnumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)` (live COM API) rather than direct registry reads. | **[VM-VERIFIED]** | ✅ Confirmed — registry showed stale Active state for manually disabled devices even after service restart and reboot |
| Friendly name is readable via `PKEY_Device_FriendlyName` = `{a45c254e-df1c-4efd-8020-67d146a850e0},2` in the device's `Properties` subkey. Returns human-readable strings (e.g. "Altavoces", "Loop-back 1/2"). | **[VM-VERIFIED]** | ✅ Confirmed |
| Multiple active output devices (3) confirmed simultaneously on test VM. `GetDefaultAudioEndpoint(eRender, eConsole)` will return a meaningful friendly name for the consent dialog. | **[VM-VERIFIED]** | ✅ Confirmed — consent dialog naming test is valid on multi-device machines |
| Phantom/NotPresent entries (high-bit `DeviceState` flags: `0x20000004`, `0x10000004`) are abundant in the registry but are automatically excluded by `IMMDeviceEnumerator` — no extra filtering needed in `AudioDeviceService`. | **[VM-VERIFIED]** | ✅ Confirmed |

## 9. Open items checklist (feeds T0.1 VM session)

Items below now have **documented expectations** (cite) where available — the VM session confirms reality matches the docs:

- [ ] Confirm `HKLM\SOFTWARE\EqualizerAPO\ConfigPath` data on Win10 and Win11 (key + value name are [DOC], config-ref §Expression commands)
- [ ] Capture full `FxProperties` diff for one render + one capture device, before/after Configurator enable (export `.reg`) — expected value names per dev-ref §Registry changes item 4 (`{d04e05a6-…},1/,2/,5/,6`, `{d3993a3f-…},5/,6`)
- [ ] Inspect `HKLM\SOFTWARE\EqualizerAPO\Child APOs` after enabling a device with a vendor APO present (backup key is [DOC]; value format unknown)
- [ ] `icacls` output of `config\` post-install (§3 — still [COMMUNITY])
- [ ] Installer behavior with `/S` — incl. whether the documented Configurator launch (install-ref step 3) happens, and reboot flag
- [ ] Read Configurator source: CLI argument parsing, `Child APOs` value format, exact CLSID
- [ ] `Device:` selector test: GUID pattern, duplicate names, case sensitivity, and scope reset — verify `Device: all` before `# END micgain` prevents leakage into user content below our region
- [ ] `Include:` relative-path resolution base (documented only for `Convolution:`)
- [ ] Positive `Preamp:` values + decimal/locale format acceptance
- [ ] Hot-reload latency on a capture device (slider → audible change timing)
- [ ] Control Panel enhancements pitfall: registry representation + detectability (§8)
