# CLI

The `kytyfixer` executable has no GUI dependency. Run `dotnet run --project src/Thanos.Cli -c Release -- help` from the repository root. Phase 2 adds diagnose, binary-info, map-address, compare-builds, diff-binaries, knowledge import/export and research snapshot export. analyze preserves the original wire format. Unknown commands return usage exit 2; malformed input returns 3. Core behavior is shared with desktop; tools/validate_schemas.py exercises actual commands/schema/storage and published outputs. See root README.md and docs/PHASE2.md for examples.
