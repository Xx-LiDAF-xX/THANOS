# Phase 3: external-AI requests

Version 0.3.0 generates deterministic requests for an external AI from a research snapshot. It contacts no model, executes no returned script and makes no game changes. The same Core generator serves CLI and Avalonia. Existing analysis, research and AIPrompt v1 contracts are retained; evidence-package v1 adds a manifest.

## Generate and export

```sh
kytyfixer generate-prompt ./Game.zip --log ./Logs.zip --mode diagnose --output ./request.md --bundle ./evidence.zip
kytyfixer generate-prompt ./analysis.json --mode diagnose
kytyfixer diagnose ./Game --log ./kyty.log > research.json
kytyfixer generate-prompt --report ./research.json --mode portPatch --bundle ./port-evidence.zip
```

Fresh generation accepts optional `--kyty`, `--related` and either `--knowledge` or `--database`, as `diagnose` does. A database also saves the research snapshot. A positional `.json` path imports an AnalysisReport or ResearchReport snapshot; `--report` makes this choice explicit for any filename. Basic analysis snapshots receive a log-based diagnosis but no binary/knowledge/related-build credit. Saved-report generation accepts only `--mode`, `--output` and `--bundle`; it uses the saved evidence and does not reopen game files. It rejects duplicate/missing/null wire fields, unknown properties/enums, unsupported nested versions, invalid hashes/confidence/sizes, mismatched binary/log hashes, incomplete raw-line inventories and diagnosis failures not matching their source log. It does not independently authenticate source metadata, severity, provenance or root-cause claims.

The default mode is `diagnose`. Names are case insensitive; numeric modes and unknown/duplicate/incomplete options are usage errors. JSON on stdout is the AIPrompt contract with mode, text, readiness, missingEvidence and evidence. `--output` writes UTF-8 prompt text; `--bundle` writes a ZIP. Each export commits through a temporary file in the destination directory and refuses to replace an existing destination. Multiple requested exports are independent: an earlier successful export remains if a later export fails.

In the desktop, Analyze collects a research snapshot. Select a mode and Generate prompt to inspect the AI request tab, readiness and missing evidence. Copy prompt, Export prompt and Evidence ZIP use that snapshot. Input changes invalidate both research and prompt; mode changes invalidate the prompt. Results from analysis completed for changed inputs cannot become exportable. JSON copy/export retains the research format. Native appearance/dialog behavior remains a manual acceptance check on each OS; view-model state is tested without launching the GUI.

## Modes

| Mode | Objective |
| --- | --- |
| diagnose | Earliest failure, hypotheses, alternatives and missing evidence |
| findExistingFix | Attributed historical fixes, exact build context and applicability proof |
| writePatch | Justified modification layer and a validated reproducible proposal |
| portPatch | Source implementation and corresponding target code; logical change rather than offsets |
| compareBuilds | Exact hashes/code/provenance; alternatives such as shared engine/library code |
| investigateEmulatorIssue | Matching Kyty source revision and loader/library/shader/renderer investigation |
| optimizePerformance | Measured baseline and stability before reproducible performance experiments |
| deepReverseEngineering | Targeted instruction/control-flow investigation; inaccessible evidence remains UNKNOWN |

Every request covers role, game/family/title IDs, game version/build/engine/hash, emulator identity/capabilities, hardware, complete raw log/events, root-cause candidates, binary findings, related builds, historical fixes, logical patch families, portability, patchability and verification. Unknown fields stay unknown. Source claims, observed evidence, hypotheses and verified outcomes must be distinguished. Shader PCs, host exception PCs and game-file offsets cannot be equated without mappings.

Optional patch proposals require target file/version/hash, signature, original/replacement bytes, location and address space, expected match count, logical change/reason, related-build applicability, verification and risk. Formats may include JSON, hex, Python/C#, binary diff, source or Kyty-compatible proposals. Phase 3 does not validate, import or run any such output. Write/port modes always identify missing validated patch targets/bytes; port mode also requires the source implementation. Performance mode always requires measured runs because this phase has no runner.

## Evidence readiness

The score is the sum of available checklist weights divided by 100. It is **evidence availability**, separate from diagnosis confidence, compatibility, provenance authenticity and authorization to patch. Scores are mode-independent so the same snapshot is comparable; mode-specific gaps appear separately in missingEvidence. Even 100% generic readiness cannot justify patch execution or prove causality.

| Criterion | Weight | Availability condition |
| --- | ---: | --- |
| Game identity | 10 | Exact executable hash and supplied title/name/family |
| Version/build | 10 | Explicit game version or build |
| Kyty build | 10 | Executable hash and explicit version; detector currently commonly returns UNKNOWN |
| Hardware | 10 | Log reports CPU, GPU, driver and RAM |
| Root-cause evidence | 10 | Nonzero-confidence hypothesis with facts and source evidence |
| Failing log | 15 | Hashed full log with non-termination error |
| Binary evidence | 15 | Inspected accessible code, complete code inventory, matching fingerprint |
| Related builds | 5 | Strong/exact-family relationship with evidence |
| Known fixes | 5 | Historical fix with confirmed/tool-verified source status; still an unauthenticated claim |
| Verification plan | 10 | Generated baseline/copy/reproduction/regression plan, not executed verification |

A snapshot with no evidence scores 10% because the request supplies a plan. Partial code inventory earns no binary credit. Similar title IDs earn no related-build credit. Unverified fixes earn no fix credit. The verification plan asks for BOOT, MENU, GAMEPLAY, CRASH REPRODUCTION, repeated STABILITY and measured PERFORMANCE, with observable acceptance criteria and INCONCLUSIVE when execution proof is absent.

## Evidence package

A ZIP contains exactly `prompt.md`, `prompt.json`, `research.json` and `manifest.json`. The manifest gives SHA-256 and byte length for the three evidence artifacts plus template version, mode and limitations. It does not hash itself, bundle executable game files or provide publisher authentication. Export regenerates the prompt to reject a mismatched snapshot/mode pair. Full raw evidence is embedded without truncation; RAM, runtime string capacity and disk remain practical constraints.

Review local paths, hardware details and raw logs before sharing the package with an external service. Embedded data is labeled untrusted and JSON-escaped; instructions inside a log are evidence, not instructions to follow. External model behavior is outside THANOS control.

## Verification

Automated tests cover all modes, conservative readiness, full long-line/raw-log preservation, strict saved-report rejection, package digests, overwrite/cancellation cleanup and stale desktop exports. CLI integration validates all modes and package schemas, fresh/saved generation parity and negative options against both build and publication outputs. These are synthetic application checks, not real game compatibility or patch verification.
