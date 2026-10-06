-- Phase 1 storage and forward-looking normalized hierarchy. No seeded compatibility claims.
CREATE TABLE game_families (
    id TEXT PRIMARY KEY, name TEXT NOT NULL,
    json TEXT NOT NULL CHECK(json_valid(json)),
    provenance_json TEXT NOT NULL CHECK(json_valid(provenance_json))
);
CREATE TABLE titles (
    id TEXT PRIMARY KEY, family_id TEXT NOT NULL REFERENCES game_families(id),
    title_id TEXT NOT NULL, region TEXT, UNIQUE(family_id, title_id, region)
);
CREATE TABLE builds (
    id TEXT PRIMARY KEY, title_id TEXT NOT NULL REFERENCES titles(id),
    game_version TEXT, build_version TEXT
);
CREATE TABLE executables (
    sha256 TEXT PRIMARY KEY CHECK(length(sha256)=64),
    build_id TEXT REFERENCES builds(id), fingerprint_json TEXT NOT NULL CHECK(json_valid(fingerprint_json))
);
CREATE TABLE kyty_builds (
    sha256 TEXT PRIMARY KEY CHECK(length(sha256)=64),
    fingerprint_json TEXT NOT NULL CHECK(json_valid(fingerprint_json))
);
CREATE TABLE patch_families (
    id TEXT PRIMARY KEY, game_family_id TEXT NOT NULL REFERENCES game_families(id),
    logical_fix TEXT NOT NULL, provenance_json TEXT NOT NULL CHECK(json_valid(provenance_json))
);
CREATE TABLE patch_implementations (
    id TEXT PRIMARY KEY, family_id TEXT NOT NULL REFERENCES patch_families(id),
    executable_sha256 TEXT NOT NULL REFERENCES executables(sha256),
    proposal_json TEXT NOT NULL CHECK(json_valid(proposal_json))
);
CREATE TABLE knowledge_entries (
    id TEXT PRIMARY KEY, family_id TEXT REFERENCES game_families(id), kind TEXT NOT NULL,
    payload_json TEXT NOT NULL CHECK(json_valid(payload_json)),
    provenance_json TEXT NOT NULL CHECK(json_valid(provenance_json)),
    status TEXT NOT NULL CHECK(status IN ('unverified','communityReported','repeated','confirmed','toolVerified'))
);
CREATE TABLE verification_results (
    id TEXT PRIMARY KEY, executable_sha256 TEXT NOT NULL REFERENCES executables(sha256),
    kyty_sha256 TEXT NOT NULL REFERENCES kyty_builds(sha256),
    patch_id TEXT REFERENCES patch_implementations(id), hardware_json TEXT NOT NULL CHECK(json_valid(hardware_json)),
    result_json TEXT NOT NULL CHECK(json_valid(result_json)), provenance_json TEXT NOT NULL CHECK(json_valid(provenance_json))
);
CREATE TABLE analyses (
    id TEXT PRIMARY KEY, created_utc TEXT NOT NULL,
    schema_version INTEGER NOT NULL CHECK(schema_version=1), report_json TEXT NOT NULL CHECK(json_valid(report_json))
);
CREATE INDEX knowledge_family ON knowledge_entries(family_id, kind);
CREATE INDEX results_matrix ON verification_results(executable_sha256, kyty_sha256, patch_id);
PRAGMA user_version=1;
