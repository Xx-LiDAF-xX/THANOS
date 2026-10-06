# THANOS

Open-source, cross-platform evidence collection and compatibility research for games tested through Kyty. GPL-3.0-or-later.

**Status: Phase 1 / 0.1.0 foundation.** The CLI and Avalonia desktop shell share the same analysis services. There is no embedded AI, emulator execution, diagnosis engine, patch import, or patch application in this release.

## What works

- Analyze a directory or accessible file without proof-of-origin checks; choose an explicit file when executable discovery is ambiguous.
- Compute SHA-256 and file size; inspect little/big-endian ELF32/ELF64 headers and tag entry points as virtual addresses.
- Inventory accessible game modules, without following directory symlinks.
- Fingerprint a selected Kyty file without executing it. Version and capabilities remain UNKNOWN until supported detection is implemented.
- Parse bounded UTF-8 logs, preserve raw text/hash and line order, identify initial error categories, capture log-reported metadata and hardware.
- Export JSON and store/retrieve evidence reports in a migrated SQLite database.
- Use the desktop shell to select paths, analyze, export and copy evidence JSON.

Unknown identities, engines, versions and families remain null. Lexical log labels are heuristic observations, not root-cause conclusions. A parsed log title ID is unverified evidence; it is not silently assigned to the game's fingerprint. Protected or unfamiliar files still receive a hash and partial analysis.

## Build and test

Install the .NET SDK version in `global.json` (10.0.401). Windows x64, Linux x64, macOS x64 and macOS ARM64 are CI targets. No WPF, Windows-only target framework, or platform-specific core APIs are used.

```sh
dotnet restore THANOS.sln --locked-mode
dotnet build THANOS.sln -c Release --no-restore
dotnet test THANOS.sln -c Release --no-build
dotnet format THANOS.sln --no-restore --verify-no-changes
```

For schema and CLI integration checks, install Python 3.13 or newer:

```sh
python -m pip install -r tools/requirements.txt
python tools/generate_schemas.py --check
python tools/validate_schemas.py
```

Run the desktop application:

```sh
dotnet run --project src/Thanos.Desktop -c Release --no-build
```

Run the CLI:

```sh
dotnet run --project src/Thanos.Cli -c Release --no-build -- help
dotnet run --project src/Thanos.Cli -c Release --no-build -- fingerprint ./Game
dotnet run --project src/Thanos.Cli -c Release --no-build -- detect-kyty ./Kyty
dotnet run --project src/Thanos.Cli -c Release --no-build -- parse-log ./examples/sample-kyty.txt
dotnet run --project src/Thanos.Cli -c Release --no-build -- analyze ./Game --kyty ./Kyty --log ./kyty.log --database ./knowledge.db
dotnet run --project src/Thanos.Cli -c Release --no-build -- database init ./knowledge.db
dotnet run --project src/Thanos.Cli -c Release --no-build -- database export ./knowledge.db ANALYSIS_ID
```

Replace paths with local inputs. The sample log is explicitly synthetic, with no claim of real game compatibility. JSON goes to stdout; diagnostics and saved analysis IDs go to stderr. Redirect stdout to export a report. Exit codes: `0` collected evidence (possibly partial), `2` invalid usage/unimplemented command, `3` input/storage error, `130` cancellation.

## Packaging

Phase 1 CI validates framework-dependent publications on each host and uploads a ZIP with CLI, desktop and license notices. End users need the .NET 10 runtime, not the SDK. Python is only for contributor checks.

```sh
dotnet publish src/Thanos.Cli -c Release --no-restore --self-contained false -o artifacts/cli
dotnet publish src/Thanos.Desktop -c Release --no-restore --self-contained false -o artifacts/desktop
python tools/validate_schemas.py --cli artifacts/cli/kytyfixer.dll
python tools/package_artifacts.py artifacts
```

Self-contained native ZIP/installers, AppImage/tar.gz, .app/DMG, signing and release automation are Phase 7 work. Runtime-independent checksums are not claimed: ZIP metadata and platform-dependent outputs can differ. SDK pinning, dependency locks and deterministic compilation provide the initial reproducibility controls.

## Documentation

Start with [ARCHITECTURE.md](ARCHITECTURE.md) for the repository tree, diagrams, contracts, phase boundaries and storage hierarchy. [docs/PHASE1.md](docs/PHASE1.md) indexes every first-task deliverable. [schemas/README.md](schemas/README.md) documents wire formats; [ROADMAP.md](ROADMAP.md) defines subsequent milestones. Contribution and security guidelines are in [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

Inputs are read-only. Archives are hashed as ordinary files and are not extracted. Unrecognized containers do not imply successful code inspection or decryption. Application JSON Lines logs use the platform's local application-data directory under `THANOS/logs`; full input paths and game bytes are not written to these logs. Exported analysis reports do contain local paths and raw user-supplied logs.

The repository is independent of Kyty and is not an official emulator project. Analyze-only mode does not require an installed emulator. THANOS running on an OS does not establish that a particular Kyty build can run there.
