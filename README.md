# THANOS

Open-source, cross-platform evidence collection and compatibility research for games tested through Kyty. GPL-3.0-or-later.

**Status: Phase 4 / 0.4.0 patch validation and working-copy transactions.** The CLI and Avalonia desktop shell share evidence, research and patch transaction services. Phase 2 adds accessible ELF analysis, error correlation, root-cause candidates, binary relationships and a provenanced local knowledge base. Phase 3 adds eight prompt modes, weighted evidence readiness and prompt/evidence ZIP export. Phase 4 adds AI patch normalization, guarded ELF validation, working-copy backups and atomic transactions with rollback. Emulator execution and runtime verification remain later phases.

## What works

- Analyze a directory or accessible file without proof-of-origin checks; choose an explicit file when executable discovery is ambiguous.
- Compute SHA-256 and file size; inspect little/big-endian ELF32/ELF64 headers and tag entry points as virtual addresses.
- Inventory accessible game modules, without following directory symlinks.
- Fingerprint a selected Kyty file without executing it. Version and capabilities remain UNKNOWN until supported detection is implemented.
- Parse complete UTF-8 logs without configured byte, line-count or line-length limits; preserve raw text/hash and line order, identify initial error categories, capture log-reported metadata and hardware.
- Read game/log ZIPs and selected archive members; support RAR/7z/tar through libarchive and Kyty ZAR through the bundled Python reader. Load PKG as partial package evidence. See [input formats and helper requirements](docs/INPUTS.md).
- Export JSON and store/retrieve evidence reports in a migrated SQLite database.
- Use the desktop shell to select paths, analyze, generate any prompt mode, inspect readiness/missing evidence, copy/export JSON, prompt text or an evidence ZIP, and inspect, validate, apply or roll back patches.
- Inspect accessible ELF32/64 sections, segments, symbols, dynamic imports/exports, relocations, ASCII strings, executable-section hashes and symbol-bounded function hashes.
- Map file, virtual and runtime addresses through unique PT_LOAD mappings; runtime mapping requires an explicit load bias. Zero-filled memory cannot be mapped to file bytes.
- Match literal error signatures, identify the earliest observed error candidate and separate possible downstream effects from independent failures. Every root-cause candidate carries facts, evidence, hypotheses, confidence and a recommendation.
- Compare code evidence across builds without copying offsets or equating similar title IDs; compare original/candidate bytes as bounded ranges with context.
- Import/export versioned knowledge with families, exact-hash bindings, logical patch families, issues/fixes, error signatures, relationships and provenance. Store Phase 2 research snapshots separately from Phase 1 analyses.
- Normalize AI patch outputs from pure JSON and Markdown code fences; retain source diffs, scripts and binary patches as inspect-only without execution.
- Validate patch proposals against exact target file hashes, accessible ELF structures, unique PT_LOAD address mappings, modification bounds, exact original bytes, and exact signature match counts/locations without nearby substitution.
- Create atomic working-copy transactions with locked original reading, pristine target backups, and separate working-copy candidate binaries. Original game directories are never modified during apply.
- Inspect and roll back transactions with explicit approval checks; optionally restore pristine backups to the original root while preserving displaced bytes in recovery.

Unknown identities, engines and versions remain null. Family assignment requires explicit confirmed/tool-verified family and exact-hash binding records; those imported statuses are source claims, not independently authenticated by THANOS. Unverified family claims appear as related knowledge without assigning identity. Lexical matches are hypotheses, not established root causes or verified fixes. Protected or unfamiliar files still receive a hash and partial analysis, with log diagnosis available. Patch validation confirms structural and byte safety; compatibility remains unverified until later runtime verification phases.

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
dotnet run --project src/Thanos.Cli -c Release --no-build -- binary-info ./Game/eboot.bin
dotnet run --project src/Thanos.Cli -c Release --no-build -- diagnose ./Game --log ./kyty.log --related ./OtherBuild/eboot.bin --database ./knowledge.db
dotnet run --project src/Thanos.Cli -c Release --no-build -- map-address ./Game/eboot.bin virtualAddress 0x400100 fileOffset
dotnet run --project src/Thanos.Cli -c Release --no-build -- compare-builds ./BuildA/eboot.bin ./BuildB/eboot.bin
dotnet run --project src/Thanos.Cli -c Release --no-build -- diff-binaries ./original.elf ./candidate.elf
dotnet run --project src/Thanos.Cli -c Release --no-build -- knowledge import ./knowledge.db ./examples/knowledge.json
dotnet run --project src/Thanos.Cli -c Release --no-build -- knowledge export ./knowledge.db
dotnet run --project src/Thanos.Cli -c Release --no-build -- database export-research ./knowledge.db RESEARCH_ID
dotnet run --project src/Thanos.Cli -c Release --no-build -- generate-prompt ./Game.zip --log ./Logs.zip --mode diagnose --output ./request.md --bundle ./evidence.zip
dotnet run --project src/Thanos.Cli -c Release --no-build -- generate-prompt --report ./research.json --mode portPatch
dotnet run --project src/Thanos.Cli -c Release --no-build -- normalize-patch ./ai-output.md --output ./patch.json
dotnet run --project src/Thanos.Cli -c Release --no-build -- validate-patch ./patch.json --game ./Game
dotnet run --project src/Thanos.Cli -c Release --no-build -- validate-patch ./patch.json --game ./Game/eboot.bin --research ./research.json
dotnet run --project src/Thanos.Cli -c Release --no-build -- apply-patch ./patch.json --game ./Game --transaction ./tx-01 --approve PATCH_ID
dotnet run --project src/Thanos.Cli -c Release --no-build -- rollback ./tx-01 --approve TRANSACTION_ID
dotnet run --project src/Thanos.Cli -c Release --no-build -- rollback ./tx-01 --approve TRANSACTION_ID --restore-original ./Game
```

Replace paths with local inputs. The sample log is explicitly synthetic, with no claim of real game compatibility. JSON goes to stdout; diagnostics and saved analysis IDs go to stderr. Redirect stdout to export a report. Exit codes: `0` collected evidence (possibly partial), `2` invalid usage/unimplemented command, `3` input/storage error, `130` cancellation.

`analyze` retains the original Phase 1 JSON contract. Use `diagnose` for the new research report. `diagnose --database` reads knowledge and saves a research snapshot; `--knowledge file.json` is an alternative direct input and cannot be combined with `--database`. The desktop Analyze command emits the same Phase 2 research format and accepts optional knowledge/related-build files. Patch JSON is normalized from pure JSON or Markdown, validated against exact hashes and binary structures, and applied exclusively to isolated working copies with full rollback support.

## Packaging

CI validates framework-dependent publications on each host and uploads a versioned ZIP with CLI, desktop, source references and license notices. End users need the .NET 10 runtime, not the SDK. Python is needed for ZAR inputs and contributor checks; libarchive bsdtar is needed for RAR/7z/tar. ZIP and ordinary files need neither helper.

```sh
dotnet publish src/Thanos.Cli -c Release --no-restore --self-contained false -o artifacts/cli
dotnet publish src/Thanos.Desktop -c Release --no-restore --self-contained false -o artifacts/desktop
python tools/validate_schemas.py --cli artifacts/cli/kytyfixer.dll
python tools/package_artifacts.py artifacts
```

Self-contained native ZIP/installers, AppImage/tar.gz, .app/DMG, signing and release automation are Phase 7 work. Runtime-independent checksums are not claimed: ZIP metadata and platform-dependent outputs can differ. SDK pinning, dependency locks and deterministic compilation provide the initial reproducibility controls.

## Documentation

Start with [docs/PHASE4.md](docs/PHASE4.md) for patch validation and working-copy transactions, [docs/PHASE3.md](docs/PHASE3.md) for prompts and evidence exports, and [docs/PHASE2.md](docs/PHASE2.md) for research commands, architecture, knowledge semantics and binary limits. [ARCHITECTURE.md](ARCHITECTURE.md) describes the foundation; [docs/PHASE1.md](docs/PHASE1.md) retains the first-task deliverable index. [schemas/README.md](schemas/README.md) documents wire formats; [ROADMAP.md](ROADMAP.md) defines subsequent milestones. Contribution and security guidelines are in [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

Inputs are read-only. Selected archive members are decoded into temporary spools; their hashes refer to the complete decompressed member. PKG is partial package evidence. Unrecognized containers do not imply successful code inspection or decryption. Application JSON Lines logs use the platform's local application-data directory under `THANOS/logs`; full input paths and game bytes are not written to these logs. Exported analysis reports do contain local paths and raw user-supplied logs.

The repository is independent of Kyty and is not an official emulator project. Analyze-only mode does not require an installed emulator. THANOS running on an OS does not establish that a particular Kyty build can run there.
