-- Additive migration: preserves all version 1 analyses and normalized knowledge tables.
CREATE TABLE research_reports (
    id TEXT PRIMARY KEY, created_utc TEXT NOT NULL,
    schema_version INTEGER NOT NULL CHECK(schema_version=1),
    report_json TEXT NOT NULL CHECK(json_valid(report_json))
);
CREATE TABLE family_bindings (
    executable_sha256 TEXT PRIMARY KEY CHECK(length(executable_sha256)=64),
    family_id TEXT NOT NULL REFERENCES game_families(id),
    binding_json TEXT NOT NULL CHECK(json_valid(binding_json))
);
CREATE TABLE error_signatures (
    id TEXT PRIMARY KEY, signature_json TEXT NOT NULL CHECK(json_valid(signature_json))
);
CREATE TABLE family_relationships (
    left_build TEXT NOT NULL, right_build TEXT NOT NULL,
    relationship_json TEXT NOT NULL CHECK(json_valid(relationship_json)),
    PRIMARY KEY(left_build,right_build)
);
ALTER TABLE patch_families ADD COLUMN family_json TEXT CHECK(family_json IS NULL OR json_valid(family_json));
PRAGMA user_version=2;
