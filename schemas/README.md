# Version 1 wire contracts

Schemas use JSON Schema draft 2020-12. All serialized model fields are required in the wire format; unknown scalar values are explicit nulls, absent evidence collections are empty, and every top-level record has `schemaVersion: 1`. Enums are camelCase strings except verification scenarios and the detector's documented `Unknown`/`Detected` status.

| C# model | Schema |
| --- | --- |
| GameFingerprint | game-schema-v1.json |
| GameFamily | game-family-schema-v1.json |
| KytyFingerprint | kyty-fingerprint-schema-v1.json |
| ParsedLog | parsed-log-schema-v1.json |
| ErrorEvent | error-event-schema-v1.json |
| RootCause | diagnosis-schema-v1.json |
| PatchFamily | patch-family-schema-v1.json |
| PatchProposal | patch-schema-v1.json |
| AIPrompt | ai-prompt-schema-v1.json |
| EvidencePackageManifest | evidence-package-schema-v1.json |
| VerificationResult | verification-schema-v1.json |
| ErrorSignature | error-signature-schema-v1.json |
| FamilyRelationship | family-relationship-schema-v1.json |
| AnalysisReport | analysis-schema-v1.json |
| Evidence, Provenance, BinaryAddress, BinarySection, BinarySegment, KytyCapability, PatchCheck | common-schema-v1.json definitions |
| BinaryInspection | binary-inspection-schema-v1.json |
| AddressMappingResult | address-mapping-schema-v1.json |
| DiagnosisReport | diagnosis-report-schema-v1.json |
| KnowledgeEntry | knowledge-entry-schema-v1.json |
| KnowledgeBundle | knowledge-bundle-schema-v1.json |
| ResearchReport | research-schema-v1.json |
| BinaryDifference | binary-difference-schema-v1.json |
| PatchImportResult | patch-import-schema-v1.json |
| PatchValidationResult | patch-validation-schema-v1.json |
| PatchTransaction | patch-transaction-schema-v1.json |

Phase 2 introduces separate research contracts and adds common definitions without changing existing Phase 1 record properties. Phase 3 adds `evidence-package` and manifests. Phase 4 preserves `PatchProposal` v1 and introduces `patch-import`, `patch-validation`, and `patch-transaction` result contracts, with `PatchCheck` in `common-schema-v1.json`.

SHA-256 is lowercase hex. Addresses carry `addressSpace` and a hex `value`; no implicit file/runtime conversion is permitted. Patch `location` replaces the untyped offset/addressType pair in the illustrative specification. Signature tokens are hex bytes or `??`, separated by one space; original/replacement bytes are hex bytes without wildcards.

Confidence/readiness are 0–1 fractions. The absence of a recorded fix is not evidence that no fix exists. Provenance status is never promoted automatically. RootCause and logical PatchFamily records are used in Phase 2. AIPrompt generation is implemented in Phase 3; evidence-package v1 adds a SHA-256/byte-length manifest with PackageEntry common definitions. Phase 4 adds dedicated patch normalization, guarded validation against accessible ELF binaries, and isolated working-copy transactions with rollback. Phase 5 implements live and offline VerificationResult evaluation across levels 0–7 against verification-schema-v1.json.

The `$id` namespace identifies files in the public repository. Validation tools register every schema locally and resolve references offline; validators never need to download schemas. Edit tools/generate_schemas.py and run it to regenerate. CI checks schema drift, C# property alignment, sample data, JSON export and CLI/storage integration.
