# CLI

The `kytyfixer` executable has no GUI dependency. Run `dotnet run --project src/Thanos.Cli -c Release -- help` from the repository root. Commands emit evidence JSON on stdout with errors/IDs on stderr. Supported commands are fingerprint, detect-kyty, parse-log, analyze and database init/export. Unknown commands return usage exit 2. Core behavior is shared with desktop; tools/validate_schemas.py exercises actual command invocations, schema checks and SQLite export. See root README.md for exact examples.
