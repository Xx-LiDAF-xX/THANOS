# Contributing

THANOS welcomes evidence-driven, cross-platform contributions. Use a fork and a focused branch; describe the concrete problem, resulting behavior and validation in a pull request. Use semantic versioning and update CHANGELOG.md for user-visible changes.

Read README.md, ARCHITECTURE.md and ROADMAP.md before editing. Keep shared behavior in Core/services and platform integration in adapters. Do not duplicate CLI and GUI logic. Inspect existing implementation, preserve unrelated behavior and maintain versioned schema compatibility. Discuss expansion beyond the current milestone in an issue.

Install .NET SDK 10.0.401 and Python 3.13+ for contributor tools. Run exactly:

```sh
dotnet restore THANOS.sln --locked-mode
dotnet build THANOS.sln -c Release --no-restore
dotnet test THANOS.sln -c Release --no-build
dotnet format THANOS.sln --no-restore --verify-no-changes
python -m pip install -r tools/requirements.txt
python tools/generate_schemas.py --check
python tools/validate_schemas.py
```

For intentional schema changes, edit tools/generate_schemas.py, regenerate schemas and add migration/backwards-compatibility checks. Required wire properties must track the C# models. Preserve old schema files when introducing version 2. Do not change version 1 incompatibly after release. JSON Schema validation is structural; executable-address and patch-byte safety require dedicated validators in Phase 4.

Use small synthetic binaries/logs for tests; never contribute game dumps, keys or proprietary code. Test every major feature, including malformed inputs and UNKNOWN results. Record which OSes you actually tested. CI checks Windows, Linux and both macOS architectures. Do not claim that CI passed before examining its results.

Dependency updates must include package locks, licenses and notices. Restore normally after intentionally changing package versions, regenerate notices with tools/collect_notices.py, then validate locked restore. Do not disable NuGet auditing to bypass a vulnerable dependency. Review native dependency notices as well as managed package license expressions.

Every compatibility claim needs source, date, verification status, game/Kyty version and hardware where known. Community reports remain community reports. A title ID, similar title or offset alone is not evidence of a shared codebase. Never invent patch bytes or success claims.

Contributions are licensed under GPL-3.0-or-later. You must have the right to contribute the code and supporting material. Follow CODE_OF_CONDUCT.md and report sensitive vulnerabilities using SECURITY.md.
