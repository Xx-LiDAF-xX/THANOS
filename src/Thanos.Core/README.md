# Core

Dependency-free models, interfaces and analysis orchestration. Models.cs defines versioned evidence/future contracts; Contracts.cs documents the public boundaries in compiling code. AnalysisService calls only the Phase 1 fingerprint, detector and log interfaces. Example usage is ServiceFactory.CreateAnalysisService().AnalyzeAsync(game, kyty, log). Evidence and schema contract tests cover this subsystem. See ARCHITECTURE.md and schemas/README.md for API/wire semantics.
