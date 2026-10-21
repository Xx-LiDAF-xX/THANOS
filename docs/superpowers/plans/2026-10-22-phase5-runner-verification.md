# Phase 5: Kyty Runner and Verification Levels 0-7 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver Phase 5 of THANOS (v0.5.0): build-specific Kyty capability adapters, a robust streaming process runner with clean process-tree termination, scenario definitions (BOOT, MENU, GAMEPLAY, CRASH REPRODUCTION, STABILITY, PERFORMANCE), incremental stdout/stderr/log capture without configured limits, baseline vs. candidate log comparison, and verification level 0-7 outcome evaluation meeting schema `verification-schema-v1.json`.

**Architecture:** Build-specific capability adapters translate detected Kyty capabilities into safe, un-guessed process requests. A streaming runner executes the emulator, incrementally capturing stdout, stderr, and log output without buffer limits while guaranteeing clean tree termination on timeout or cancellation. A verification engine inspects captured execution evidence, classifies achieved verification levels (0-7), evaluates baseline vs. candidate runs, and generates schema-compliant `VerificationResult` records with provenance.

**Tech Stack:** C# 13 / .NET 10, Avalonia UI, Microsoft.Data.Sqlite, System.Diagnostics.Process, JSON Schema draft 2020-12, Python 3.

**Spec:** [docs/PHASE5.md](file:///c:/Users/TAPIOCA/Desktop/repos/THANOS/docs/PHASE5.md) and [ROADMAP.md](file:///c:/Users/TAPIOCA/Desktop/repos/THANOS/ROADMAP.md)

## Global Constraints

- Never invent or guess CLI options or arguments for Kyty builds; argument generation must derive strictly from detected build capabilities or explicit user input.
- Real scenario evidence is mandatory for `PASS`; missing required log proof produces `INCONCLUSIVE`.
- Streaming stdout, stderr, and log capture must not apply configured byte or line limits.
- Process runner must cleanly terminate the entire child process tree on timeout or cancellation without leaving orphaned emulator processes.
- All exported JSON records must conform strictly to `verification-schema-v1.json` and existing common schemas with `schemaVersion: 1`.
- SQLite storage must be strictly additive and reject overwriting or mutating confirmed verification records.
- Cross-platform compatibility: Windows x64, Linux x64, macOS x64, macOS ARM64.

## Review Focus

1. **Process timeout / cancellation leak**: When an emulator hangs or is cancelled, all guest threads/child processes must be terminated immediately with `Kill(entireProcessTree: true)`.
2. **Missing required log patterns**: If an emulator exits with code 0 but does not emit the required milestone logs (e.g. loader/menu), the outcome must be `Inconclusive`, never `Pass`.
3. **Guessed CLI flags**: If a Kyty fingerprint does not indicate capability for a flag (e.g. `--headless` or `--zar`), the adapter must not generate that argument.
4. **Log file race / buffering**: Reading active log files while Kyty writes them must use shared read permissions (`FileShare.ReadWrite | FileShare.Delete`) and handle partial line writes.
5. **Schema drift**: Generated `VerificationResult` JSON must pass offline draft 2020-12 validation with lowercase hex SHA-256 and valid scenario/outcome enums.

---

### Task 1: Core Models, Scenario Definitions, and Verification Contracts

**Files:**
- Modify: `src/Thanos.Core/Models.cs`
- Modify: `src/Thanos.Core/Contracts.cs`
- Test: `tests/Thanos.Tests/VerificationModelTests.cs`

**Interfaces:**
- Consumes: `VerificationResult`, `TestOutcome`, `KytyFingerprint`, `Provenance` from `Thanos.Core`
- Produces: `ScenarioType`, `VerificationLevel`, `ScenarioDefinition`, `IVerificationService`, `IVerificationEngine`

- [ ] **Step 1: Write failing test for scenario definitions and verification level metadata**

Create `tests/Thanos.Tests/VerificationModelTests.cs` verifying `ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot)` returns expected level, default timeout, and required patterns.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter VerificationModelTests`
Expected: FAIL due to missing types.

- [ ] **Step 3: Implement Scenario models and contracts in `Models.cs` and `Contracts.cs`**

Add `ScenarioType` (`Boot`, `Menu`, `Gameplay`, `CrashReproduction`, `Stability`, `Performance`), `VerificationLevel` enum or constants (0 to 7), `ScenarioDefinition` record with required and failure patterns, and `IVerificationService` interface.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter VerificationModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Core tests/Thanos.Tests
git commit -m "feat: add Phase 5 scenario models and verification contracts"
```

---

### Task 2: Build-Specific Kyty Capability Adapters

**Files:**
- Create: `src/Thanos.Infrastructure/KytyAdapters.cs`
- Modify: `src/Thanos.Core/Contracts.cs`
- Test: `tests/Thanos.Tests/KytyAdapterTests.cs`

**Interfaces:**
- Consumes: `IKytyAdapter`, `KytyFingerprint`, `KytyCapability`
- Produces: `IKytyAdapterRegistry`, `DefaultKytyAdapter`, `KytyPs5Adapter`

- [ ] **Step 1: Write failing tests for Kyty capability adapters**

Create `tests/Thanos.Tests/KytyAdapterTests.cs` testing:
- Default adapter generates `--game <path>` and rejects unknown flags.
- KytyPS5 adapter respects `.zar` capabilities and explicit settings files without guessing unsupported flags.
- Adapter registry selects the appropriate adapter for a given fingerprint.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KytyAdapterTests`
Expected: FAIL due to missing adapter implementations.

- [ ] **Step 3: Implement `KytyAdapters.cs`**

Implement `DefaultKytyAdapter`, `KytyPs5Adapter`, and `KytyAdapterRegistry`. Ensure that options are never guessed: if a capability is not detected, flags dependent on it are omitted.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter KytyAdapterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Infrastructure/KytyAdapters.cs tests/Thanos.Tests/KytyAdapterTests.cs
git commit -m "feat: implement build-specific Kyty capability adapters"
```

---

### Task 3: Streaming Process Runner with Clean Tree Termination

**Files:**
- Create: `src/Thanos.Infrastructure/StreamingProcessRunner.cs`
- Modify: `src/Thanos.Core/Contracts.cs`
- Test: `tests/Thanos.Tests/ProcessRunnerTests.cs`

**Interfaces:**
- Consumes: `ProcessRequest`, `ProcessResult`, `IProcessRunner`
- Produces: `IStreamingProcessRunner`, `StreamingProcessResult`, incremental output events/callbacks

- [ ] **Step 1: Write failing test for streaming process runner**

Create `tests/Thanos.Tests/ProcessRunnerTests.cs` testing:
- Incremental capture of stdout and stderr without line/byte truncation limits.
- Clean process tree termination when timeout or cancellation triggers.
- Accurate exit code, timing, and error handling.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ProcessRunnerTests`
Expected: FAIL.

- [ ] **Step 3: Implement `StreamingProcessRunner.cs`**

Implement `StreamingProcessRunner` using `System.Diagnostics.Process` with asynchronous event-driven stream readers, shared file reading for emulator logs, and guaranteed `Kill(entireProcessTree: true)` on timeout/cancellation.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ProcessRunnerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Infrastructure/StreamingProcessRunner.cs tests/Thanos.Tests/ProcessRunnerTests.cs
git commit -m "feat: implement streaming process runner with clean tree termination"
```

---

### Task 4: Verification Engine and Level 0-7 Outcome Evaluation

**Files:**
- Create: `src/Thanos.Core/VerificationEngine.cs`
- Create: `src/Thanos.Infrastructure/VerificationService.cs`
- Test: `tests/Thanos.Tests/VerificationEngineTests.cs`

**Interfaces:**
- Consumes: `ScenarioDefinition`, `ParsedLog`, `ProcessResult`, `VerificationResult`
- Produces: Level 0-7 classification, baseline vs. candidate run comparison (`Pass`, `Fail`, `Improved`, `Regressed`, `Inconclusive`), and schema-compliant JSON.

- [ ] **Step 1: Write failing tests for verification engine**

Create `tests/Thanos.Tests/VerificationEngineTests.cs` with test cases:
- Baseline crashed at Level 1, candidate reaches Level 3: Outcome is `Improved`.
- Candidate achieves target scenario level with all required log patterns: Outcome is `Pass`.
- Candidate missing required log proof: Outcome is `Inconclusive`.
- Candidate crashes earlier than baseline: Outcome is `Regressed`.
- Metrics calculation (durations, lines, error counts).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter VerificationEngineTests`
Expected: FAIL.

- [ ] **Step 3: Implement `VerificationEngine.cs` and `VerificationService.cs`**

Implement pattern-matching heuristics against captured logs for levels 0 through 7:
- Level 0: None / Crash before boot
- Level 1: Boot (runtime linker / subsystem initialization)
- Level 2: Title / Intro (display window created, initial frame/splash)
- Level 3: Menu (UI / main menu interaction)
- Level 4: In-Game (3D world or game level active)
- Level 5: Playable (sustained gameplay without fatal exceptions)
- Level 6: Complete (scenario milestone reached)
- Level 7: Perfect (zero warnings/glitches, target FPS)
Implement baseline vs candidate comparison logic enforcing "no proof = inconclusive".

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter VerificationEngineTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Core/VerificationEngine.cs src/Thanos.Infrastructure/VerificationService.cs tests/Thanos.Tests/VerificationEngineTests.cs
git commit -m "feat: implement verification level 0-7 engine and outcome evaluation"
```

---

### Task 5: SQLite Verification Store Persistence

**Files:**
- Modify: `src/Thanos.Infrastructure/Storage/SqliteKnowledgeStore.cs`
- Modify: `src/Thanos.Core/ResearchContracts.cs`
- Test: `tests/Thanos.Tests/VerificationStoreTests.cs`

**Interfaces:**
- Consumes: `VerificationResult`, `SqliteKnowledgeStore`
- Produces: `SaveVerificationAsync`, `GetVerificationAsync`, `ListVerificationsAsync`

- [ ] **Step 1: Write failing test for verification result persistence**

Create `tests/Thanos.Tests/VerificationStoreTests.cs` testing saving and retrieving `VerificationResult` in `SqliteKnowledgeStore`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter VerificationStoreTests`
Expected: FAIL due to missing store methods.

- [ ] **Step 3: Implement store methods in `SqliteKnowledgeStore.cs`**

Implement `SaveVerificationAsync`, `GetVerificationAsync`, and query methods using the existing `verification_results` table in `schema-v1.sql`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter VerificationStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Infrastructure/Storage/SqliteKnowledgeStore.cs src/Thanos.Core/ResearchContracts.cs tests/Thanos.Tests/VerificationStoreTests.cs
git commit -m "feat: support saving and loading VerificationResult in SQLite store"
```

---

### Task 6: CLI `verify` Command and Subsystem Dispatcher

**Files:**
- Modify: `src/Thanos.Cli/Program.cs`
- Test: `tests/Thanos.Tests/CliVerificationTests.cs`

**Interfaces:**
- Consumes: `IVerificationService`, `IKytyAdapterRegistry`, `IStreamingProcessRunner`
- Produces: CLI `verify` command with options `--game`, `--kyty`, `--scenario`, `--baseline-log`, `--candidate-log`, `--timeout`, `--output`, `--save`.

- [ ] **Step 1: Write failing tests for CLI `verify` command**

Create `tests/Thanos.Tests/CliVerificationTests.cs` testing syntax validation, argument parsing, offline verification from logs, execution verification with synthetic emulator runner, and JSON output formatting.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter CliVerificationTests`
Expected: FAIL.

- [ ] **Step 3: Implement `verify` command in `src/Thanos.Cli/Program.cs`**

Wire `verify` command into the CLI argument dispatcher, handling inputs, timeouts, output files, SQLite storage, and returning standard exit codes (0 = success, 2 = syntax/argument error, 3 = verification/execution error, 130 = cancellation).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter CliVerificationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Cli/Program.cs tests/Thanos.Tests/CliVerificationTests.cs
git commit -m "feat: add verify CLI command for Phase 5 verification runs"
```

---

### Task 7: Desktop UI `Verification / Runner` Tab

**Files:**
- Modify: `src/Thanos.Desktop/MainWindow.axaml`
- Modify: `src/Thanos.Desktop/MainWindow.axaml.cs`
- Modify: `src/Thanos.Desktop/ViewModels/MainViewModel.cs`
- Test: `tests/Thanos.Tests/DesktopVerificationTests.cs`

**Interfaces:**
- Consumes: `IVerificationService`, `MainViewModel`
- Produces: Verification tab with target selection, scenario dropdown, streaming output viewer, outcome summary, and result export.

- [ ] **Step 1: Write failing ViewModel test for verification workflow**

Create `tests/Thanos.Tests/DesktopVerificationTests.cs` validating `MainViewModel` commands and properties for verification runs.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DesktopVerificationTests`
Expected: FAIL.

- [ ] **Step 3: Implement UI controls and ViewModel logic**

Add the `Verification` tab to `MainWindow.axaml`, bind properties in `MainViewModel.cs`, implement streaming log updates in `MainWindow.axaml.cs`, and format `VerificationResult` JSON.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DesktopVerificationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Thanos.Desktop tests/Thanos.Tests/DesktopVerificationTests.cs
git commit -m "feat: add desktop verification and runner panel in Avalonia shell"
```

---

### Task 8: Schema Validation, Documentation, Packaging, and Version Bump (0.5.0)

**Files:**
- Modify: `Directory.Build.props` (bump version to `0.5.0`)
- Create: `docs/PHASE5.md`
- Modify: `README.md`
- Modify: `ROADMAP.md`
- Modify: `SECURITY.md`
- Modify: `CHANGELOG.md`
- Modify: `schemas/README.md`
- Modify: `tools/validate_schemas.py`
- Modify: `tools/generate_schemas.py`

- [ ] **Step 1: Update `tools/validate_schemas.py` to validate `verification-schema-v1.json` via CLI**

Add verification scenario CLI exercise and JSON schema validation against `verification-schema-v1.json`.

- [ ] **Step 2: Run schema validation tool**

Run: `python tools/validate_schemas.py`
Expected: All 26 schemas validated cleanly.

- [ ] **Step 3: Create `docs/PHASE5.md` and update repository documentation**

Document Phase 5 architecture, CLI usage, scenarios, verification levels 0-7, and security properties. Bump version to `0.5.0` in `Directory.Build.props`.

- [ ] **Step 4: Run full test suite, format check, and schema drift check**

Run:
- `dotnet test`
- `dotnet format THANOS.sln --no-restore --verify-no-changes`
- `python tools/generate_schemas.py --check`
- `python tools/validate_schemas.py`
Expected: 100% PASS with 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props docs/ README.md ROADMAP.md SECURITY.md CHANGELOG.md schemas/ tools/
git commit -m "docs: finalize Phase 5 runner and verification documentation (v0.5.0)"
```
