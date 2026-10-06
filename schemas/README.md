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
| VerificationResult | verification-schema-v1.json |
| ErrorSignature | error-signature-schema-v1.json |
| FamilyRelationship | family-relationship-schema-v1.json |
| AnalysisReport | analysis-schema-v1.json |
| Evidence, Provenance, BinaryAddress, BinarySection, BinarySegment, KytyCapability | common-schema-v1.json definitions |

SHA-256 is lowercase hex. Addresses carry `addressSpace` and a hex `value`; no implicit file/runtime conversion is permitted. Patch `location` replaces the untyped offset/addressType pair in the illustrative specification. Signature tokens are hex bytes or `??`, separated by one space; original/replacement bytes are hex bytes without wildcards. These are future contracts and are not currently accepted by an apply command.

Confidence/readiness are 0–1 fractions. The absence of a recorded fix is not evidence that no fix exists. Provenance status is never promoted automatically. RootCause, PatchFamily, PatchProposal, AIPrompt and VerificationResult contracts are for later phases and must not be confused with implemented capabilities.

The `$id` namespace identifies files in the public repository. Validation tools register every schema locally and resolve references offline; validators never need to download schemas. Edit tools/generate_schemas.py and run it to regenerate. CI checks schema drift, C# property alignment, sample data, JSON export and CLI/storage integration. The C# deserializer rejects unknown properties but is not itself a complete JSON Schema validator; external patch import will require dedicated validation in Phase 4.
