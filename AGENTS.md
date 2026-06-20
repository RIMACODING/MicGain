# AGENTS.md — Repository rules for MicGain MVP

## Project
Windows desktop utility ("MicGain MVP"): single-window WPF app that controls per-device gain by writing `Preamp:` lines into Equalizer APO config files. Detects Equalizer APO and offers a consented install if missing.

## Stack (do not deviate without an issue/discussion)
- **C# / .NET 8**, **WPF** (MVVM-lite). Not WinUI 3, not WinForms.
- Audio device enumeration: NAudio (`MMDeviceEnumerator`) or direct Core Audio COM interop. No other audio libraries.
- Registry / file IO: BCL only (`Microsoft.Win32`, `System.IO`).
- Packaging: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`.

## Repository layout
```
/src/MicGain.App         # WPF shell, ViewModels, dialogs (UI only)
/src/MicGain.Core        # services, models, state machine — NO UI references
/src/MicGain.Elevated    # optional elevated console helper (Phase 2)
/assets/installer        # bundled Equalizer APO installer + GPLv2 attribution
/tests/MicGain.Core.Tests
/docs                    # research-notes.md, architecture.md, rollback.md, smoke-checklist.md
```

## Hard rules
1. **Non-destructive config edits**: only ever write inside `# BEGIN micgain` / `# END micgain` marker regions in Equalizer APO config files. Never modify or delete user content outside markers. Malformed input files → fail safe, no write.
2. **Consent before system changes**: any install, registry write, or audio-service restart must be preceded by explicit user consent in the UI. No silent system mutations.
3. **Testability**: registry, filesystem, and audio enumeration go behind interfaces in `MicGain.Core` so unit tests run with mocks (CI has no Windows audio).
4. **Least privilege**: app manifest is `asInvoker`; elevation only for the install flow, via a separate path.
5. **No new dependencies** beyond NAudio without prior approval in an issue.
6. Core services (`ApoDetectionService`, `AudioDeviceService`, `ApoConfigService`, `ApoInstallService`) live in `MicGain.Core` and are pure C# (unit-testable, no WPF types).

## Workflow
- One task = one branch = one merge request. Branch naming: `feat/<task-id>-short-name`, `docs/...`, `spike/...`.
- Every MR description references its issue and states which acceptance criteria it satisfies.
- Unit tests required for: config parser/writer (highest coverage — corruption-sensitive), detection logic, device filtering, state machine transitions.
- Anything that requires a real Windows machine with Equalizer APO (registry verification, silent-install behavior, hot-reload latency, audio checks) **cannot be verified by agents**: document findings/assumptions in `docs/research-notes.md` and flag them clearly as `NEEDS-VM-VERIFICATION` in the MR description.
- Keep MRs small and reviewable. Prefer a draft MR with a proposal over a large unreviewed change.

## Definition of done (per task)
- Acceptance criteria from the issue met or explicitly flagged as needing VM verification.
- Tests pass locally (`dotnet test`).
- No edits outside the task's scope; no drive-by refactors.

## Reference documents (canonical — always consult before coding)
- **`docs/internal/apo-config-reference.md`** — canonical copy of the Equalizer APO configuration reference (command format, `Preamp:`, `Device:`, `Channel:`, `Include:`, filter syntax). Any code or test that parses or writes APO config files MUST conform to this document. Do NOT rely on memory or external sources for APO config syntax; if this document is ambiguous or silent on a point, flag it `NEEDS-VM-VERIFICATION` instead of guessing.
- **`docs/internal/apo-development-reference.md`** — canonical copy of the Equalizer APO developer documentation (compilation, source organization, APO registration: `FxProperties` value names, `Child APOs` backup key, `DisableProtectedAudioDG`). Any code touching APO detection or device enablement/rollback MUST conform to this document under the same no-guessing rule.
- **`docs/internal/apo-install-troubleshooting-reference.md`** — canonical copy of the Equalizer APO installation tutorial and troubleshooting guide (install flow incl. Configurator launch and reboot, default config state, Control Panel enhancements pitfalls, log file / `EnableTrace` diagnostics). Consult for `ApoInstallService` behavior and UI error/help states.
- `docs/research-notes.md` — T0.2 spike findings (registry layout, installer, Configurator, elevation strategy) with confidence labels.
- **`docs/APO_doc_REFERENCE.md`** — canonical reference on Windows Audio Processing Objects (APO) internals: APO API/framework overview, COM registration, `IAudioProcessingObject` lifecycle, SFX/MFX/EFX slot types, and related Windows audio pipeline concepts. Any code or reasoning that touches APO registration, COM interop, or the Windows audio pipeline MUST consult this document. Do NOT rely on memory or external sources for APO internals; if this document is ambiguous or silent on a point, flag it `NEEDS-VM-VERIFICATION` instead of guessing.

<!-- BEGIN sqz-agents-guidance (auto-installed by sqz init; remove this block to disable) -->

## sqz — Token-Optimized CLI Output

When running shell commands whose output may be long (directory listings,
git log/diff, test runners, build logs, `docker ps`, `kubectl get`, etc.),
pipe the output through `sqz compress` to reduce token consumption.

`sqz` is a stdin-to-stdout compressor, not a command wrapper. The correct
usage is to pipe the command's output into `sqz compress`:

```bash
# Instead of:     Use:
git status        git status 2>&1 | C:/Users/RIMA/.cargo/bin/sqz.exe compress
cargo test        cargo test 2>&1 | C:/Users/RIMA/.cargo/bin/sqz.exe compress
git log -10       git log -10 2>&1 | C:/Users/RIMA/.cargo/bin/sqz.exe compress
docker ps         docker ps 2>&1 | C:/Users/RIMA/.cargo/bin/sqz.exe compress
ls -la            ls -la 2>&1 | C:/Users/RIMA/.cargo/bin/sqz.exe compress
```

The `2>&1` captures stderr too, which is useful for commands like `cargo
test` where diagnostics go to stderr. `sqz compress` filters and compresses
the combined output while preserving filenames, paths, and identifiers.
It typically saves 60-90% tokens on verbose commands.

Do NOT pipe output for:
- Interactive commands (`vim`, `ssh`, `python`, REPLs)
- Compound commands with shell operators (`cmd && other`, `cmd > file.txt`,
  `cmd; other`) — run those directly
- Short commands whose output is already a few lines

If `sqz` is not on PATH, run commands normally.

The `sqz-mcp` MCP server is also available — Codex reads it from
`~/.codex/config.toml` under `[mcp_servers.sqz]`. It exposes three
tools: `compress` (the default pipeline), `passthrough` (return text
unchanged — the escape hatch below), and `expand` (resolve a
`§ref:HASH§` token back to the original bytes).

## Escape hatch — when sqz output confuses you

If you see a `§ref:HASH§` token and can't parse it, or compressed
output is leading you to make lots of small retries instead of one
big request, use one of these:

- **`C:/Users/RIMA/.cargo/bin/sqz.exe expand <prefix>`** — resolve a dedup ref back to the
  original bytes. Accepts bare hex (`sqz expand a1b2c3d4`) or the full
  token pasted verbatim (`sqz expand §ref:a1b2c3d4§`).
- **`SQZ_NO_DEDUP=1`** — set this env var for one command to disable
  dedup: `SQZ_NO_DEDUP=1 git status 2>&1 | sqz compress`. You'll get
  the full compressed output with no `§ref:…§` tokens.
- **`--no-cache`** — same opt-out as a CLI flag:
  `git status 2>&1 | sqz compress --no-cache`.

If you're using the MCP server, the `passthrough` tool returns raw
text and the `expand` tool resolves refs — call them when you need
data sqz hasn't touched.

<!-- END sqz-agents-guidance -->
