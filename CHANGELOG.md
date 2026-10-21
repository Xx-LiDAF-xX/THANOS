# Changelog

## 0.4.0 - Unreleased

- Added Phase 4 patch import and normalization for pure JSON, Markdown code blocks, source diffs, scripts, and binary patches; non-JSON formats remain inspection-only and are never executed.
- Added strict shape and wire normalization for PatchProposal v1, requiring unverified external-AI provenance.
- Added comprehensive binary patch validation: target resolution, SHA-256 integrity, ELF format/architecture checks, research context alignment, address mapping to unique file offsets, file-backed PT_LOAD range bounds, exact original bytes, exact signature match count and location (no nearby substitution), and distinct equal-length replacement bytes.
- Added isolated patch transaction service: atomic staging, locked original reading, pristine backup creation, separate working-copy modification, and rollback with optional explicit original target restoration and displaced-bytes recovery.
- Added CLI commands: normalize-patch, validate-patch, apply-patch, and rollback with required explicit approvals.
- Added Avalonia desktop Patch validation / transaction tab with workflow actions and modal confirmation dialogs.
- Added patch-import, patch-validation, and patch-transaction v1 JSON schemas and common PatchCheck definition.

## 0.3.0 - Unreleased

- Added eight external-AI prompt modes with complete research evidence, UNKNOWN rules and mode-specific requirements.
- Added weighted evidence readiness, missing-evidence lists and baseline/candidate verification plans.
- Added CLI fresh/saved analysis/research generation and desktop mode selection, copy/export and stale-result prevention.
- Added atomic new-file prompt/evidence ZIP export with a SHA-256/size manifest and strict saved-report consistency checks.
- Preserved existing evidence/prompt schemas and added evidence-package v1. No AI API, script execution or game modification.

## 0.2.0 — Unreleased

- Reviewed the local KytyPS5 boot/loader/filesystem/shader/logging paths and documented their implications for research.
- Removed configured log byte, line-count, line-length and signature-match caps; added incremental UTF-8 decoding/hashing and linear regex matching.
- Added ZIP game/log members, RAR/7z/tar via libarchive, Kyty ZAR via the bundled Python reader, and partial PKG package evidence; archives preserve selected-member paths and do not modify inputs.
- Corrected shader PC domains and fatal-banner handling using the reviewed Kyty log formats.

- Added bounded ELF32/64 structural inspection in both byte orders, section/program tables, extended table numbering, symbols, dynamic imports/exports, relocations, strings and code hashes.
- Added guarded address mapping and bounded original/candidate binary differences with context.
- Added literal error signatures, temporal/category correlation, independent failures and evidence-backed root-cause hypotheses with UNKNOWN fallbacks.
- Added conservative build relationships and exact-hash family bindings; similar title IDs alone establish no relationship.
- Added provenanced knowledge import/export, logical patch-family records, immutable research snapshots and an additive SQLite version 2 migration preserving old analyses.
- Added CLI research commands and shared-service Avalonia knowledge/related-build inputs.
- Added Phase 2 schemas, malformed-input/migration/provenance/relationship tests and published CLI integration checks.
- Fixed malformed-data exceptions escaping the CLI and desktop error handlers.

Prompts, executable patches, emulator execution and regression automation remain planned.

## 0.1.0 — Unreleased

- Created the .NET/Avalonia solution, shared interfaces and versioned evidence models.
- Added streaming file fingerprinting, ELF header inspection, module inventory and initial Kyty log parsing.
- Added conservative Kyty executable fingerprinting with unknown capabilities.
- Added CLI analysis, SQLite initialization/storage/export and an Avalonia evidence shell.
- Added JSON Schemas, synthetic examples, automated tests, cross-platform CI and initial package validation.
- Added GPL-3.0-or-later licensing, dependency notices and contributor documentation.

Root-cause inference, external-AI prompt generation, patch validation/application, emulator execution and regression automation remain planned.
