# MicGain MVP

A small Windows desktop utility that controls per-device audio gain by writing `Preamp:`
lines into [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) config files.
Single window: pick a device, move one slider. If Equalizer APO is missing, MicGain offers
a consented install of the bundled copy.

* **Stack**: C# / .NET 8, WPF (MVVM-lite). UI in `MicGain.App`; pure, unit-testable
  services in `MicGain.Core`.
* **Non-destructive**: MicGain only ever writes inside its own `# BEGIN micgain` /
  `# END micgain` marker regions in APO config files.
* **Least privilege**: the app runs `asInvoker`; elevation is requested only on the install
  path.

## Prerequisites

* Windows 10 or 11 (x64).
* To build: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
* To run the published exe: nothing — it is self-contained (no .NET runtime needed on the
  target machine).

## Build and test

```sh
dotnet restore MicGain.sln
dotnet build  MicGain.sln -c Release
# CI-safe unit tests (no Windows audio required):
dotnet test tests/MicGain.Core.Tests/MicGain.Core.Tests.csproj -c Release
```

## Publish (single-file, self-contained)

```sh
dotnet publish src/MicGain.App/MicGain.App.csproj \
  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output lands in `src/MicGain.App/bin/Release/net8.0-windows/win-x64/publish/`.

### Folder layout next to the exe

The build copies the bundled installer beside the exe. After publish, the `publish` folder
contains:

```
MicGain.App.exe
assets/installer/EqualizerAPO-x64-1.4.2.exe
assets/installer/NOTICE.EqualizerAPO.md
assets/installer/LICENSE.GPL-2.0.txt
```

MicGain resolves the installer at
`<exe folder>/assets/installer/EqualizerAPO-x64-1.4.2.exe`. **Keep the `assets/installer`
folder next to the exe when distributing.** If it is missing, the install flow fails safe
(`InstallerNotFound`): nothing runs and no system change is made.

> The Equalizer APO installer binary and the verbatim GPL-2.0 text are not committed in full
> here — see `assets/installer/README.md`. A maintainer must ensure both are present before
> shipping a build.

## First-run flow

1. **APO already installed** → the main window opens: choose a device, move the gain slider;
   MicGain writes the `Preamp:` value and APO hot-reloads it.
2. **APO not installed** → MicGain shows a consent dialog naming your default output device.
   On consent it runs the bundled installer (elevated, UAC prompt), guides you through the
   Equalizer APO Configurator to select that device, then (with a separate consent) restarts
   the Windows audio service. Declining at any step exits cleanly with no system changes.
   Every system change — install, registry write, audio-service restart — is individually
   consented. The `DisableProtectedAudioDG` write, if needed, is disclosed before it happens.

See `docs/rollback.md` to undo APO enablement manually.

## SmartScreen / unsigned exe

The MVP exe is **not code-signed**, so Windows SmartScreen may warn on first launch
("Windows protected your PC"). Choose **More info → Run anyway** to continue. Code signing
is tracked as post-MVP work.

## Documentation

* `docs/architecture.md` — component overview and data flow.
* `docs/smoke-checklist.md` — manual integration test matrix.
* `docs/rollback.md` — what the install writes and how to undo it.
* `docs/research-notes.md` — VM-verified registry / installer findings.
* `AGENTS.md` — repository rules.

## License

MicGain's own source: see repository. The bundled Equalizer APO is GPL-2.0 — see
`assets/installer/NOTICE.EqualizerAPO.md`.
