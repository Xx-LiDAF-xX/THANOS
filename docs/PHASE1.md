# First development task deliverables

This is the review index for the first-task specification. Work stops at Phase 1; future-service interfaces and data schemas compile but are not runtime implementations.

| # | Requested deliverable | Location |
| --- | --- | --- |
| 1 | Repository tree | ARCHITECTURE.md, repository tree |
| 2 | Architecture diagram | ARCHITECTURE.md, Phase 1 architecture |
| 3 | Dependency graph | ARCHITECTURE.md, dependency graph |
| 4 | Cross-platform architecture | ARCHITECTURE.md, cross-platform design |
| 5 | Platform abstraction interfaces | src/Thanos.Core/Contracts.cs |
| 6 | CLI architecture | ARCHITECTURE.md and src/Thanos.Cli |
| 7 | Avalonia architecture | ARCHITECTURE.md and src/Thanos.Desktop |
| 8 | C# interfaces | src/Thanos.Core/Contracts.cs |
| 9 | Data models | src/Thanos.Core/Models.cs |
| 10 | SQLite schema | src/Thanos.Infrastructure/Storage/schema-v1.sql |
| 11 | GameFingerprint schema | schemas/game-schema-v1.json |
| 12 | GameFamily schema | schemas/game-family-schema-v1.json |
| 13 | KytyFingerprint schema | schemas/kyty-fingerprint-schema-v1.json |
| 14 | ParsedLog schema | schemas/parsed-log-schema-v1.json |
| 15 | ErrorEvent schema | schemas/error-event-schema-v1.json |
| 16 | RootCause schema | schemas/diagnosis-schema-v1.json |
| 17 | PatchFamily schema | schemas/patch-family-schema-v1.json |
| 18 | PatchProposal schema | schemas/patch-schema-v1.json |
| 19 | AIPrompt schema | schemas/ai-prompt-schema-v1.json |
| 20 | VerificationResult schema | schemas/verification-schema-v1.json |
| 21 | Initial error-signature format | schemas/error-signature-schema-v1.json and examples/error-signature.json |
| 22 | Initial game-family relationship format | schemas/family-relationship-schema-v1.json and examples/family-relationship.json |
| 23 | Initial tests | tests/Thanos.Tests and tools/validate_schemas.py |
| 24 | GitHub Actions CI | .github/workflows/ci.yml |
| 25 | README skeleton and commands | README.md |
| 26 | Contributor skeleton | CONTRIBUTING.md |
| 27 | LICENSE recommendation | GPL-3.0-or-later selected; LICENSE and THIRD-PARTY-NOTICES |
| 28 | Exact build commands | README.md, build and test |
| 29 | Exact test commands | README.md, build and test |
| 30 | Development roadmap | ROADMAP.md |

## Verification record

Local Windows x64 checks include Release compilation, unit/integration tests, format verification, locked restore, offline JSON Schema validation and the published CLI/package check. This repository's CI configuration repeats these checks on Windows x64, Linux x64 and both macOS architectures. See CI run results for actual remote status; configuration alone does not establish success on other OSes.

No real game, installed Kyty build, native GUI interaction or emulator test was used to substantiate game compatibility. All bundled fixtures are synthetic. No AI model, patch application or test execution is included.

## Local workspace SDK

This Windows workspace initially had only the .NET 8 runtime. Verification used an isolated SDK at `%LOCALAPPDATA%\THANOS\dotnet`, downloaded from Microsoft's release metadata and checked against its SHA-512 digest. It did not replace the system installation. To use the README commands in a new PowerShell session on this machine:

```powershell
$env:PATH = "$env:LOCALAPPDATA\THANOS\dotnet;$env:PATH"
dotnet --version
dotnet run --project src/Thanos.Desktop -c Release --no-build
```

The SDK directory is outside the repository. Other contributors should install the SDK pinned by global.json through their usual .NET setup.
