# Bundled Equalizer APO installer (binary not committed)

Place the Equalizer APO installer binary in this folder with this exact file name:

    EqualizerAPO-x64-1.4.2.exe

`src/MicGain.App/App.xaml.cs` resolves it at runtime as
`<AppContext.BaseDirectory>/assets/installer/EqualizerAPO-x64-1.4.2.exe`, so packaging
(T3.1) must copy this folder next to the published executable. If the file is missing, the
install flow fails safe with `InstallOutcome.InstallerNotFound` — nothing is executed, no
consent prompt is shown, and no system change is made.

Download from https://sourceforge.net/projects/equalizerapo/ — version 1.4.2 x64 is the
build VM-verified in `docs/research-notes.md` §11.

**Licensing**: Equalizer APO is GPLv2. Before shipping, this folder must also contain the
GPLv2 license text and attribution, and we must offer source availability per GPL §3 (see
`docs/research-notes.md` §4). Installer binaries are intentionally not committed to the
repository.
