namespace Thanos.Core;

public enum AnalysisStatus { Partial, Complete }
public enum AddressSpace { FileOffset, VirtualAddress, RuntimeAddress, RelativeAddress }
public enum Severity { Information, Warning, Error, Fatal }
public enum ErrorCategory { Boot, Elf, Loader, Module, Syscall, Memory, Thread, Timing, Filesystem, Graphics, Vulkan, Shader, Gpu, Driver, Audio, Video, Network, Input, Presentation, GamePatch, VersionMismatch, Performance, Environment, Unknown }
public enum VerificationStatus { Unverified, CommunityReported, Repeated, Confirmed, ToolVerified }
public enum RelationshipStrength { ExactFamily, Strong, Probable, Weak, Unknown }
public enum TestOutcome { Pass, Fail, Improved, Regressed, Inconclusive }
public enum PromptMode { Diagnose, FindExistingFix, WritePatch, PortPatch, CompareBuilds, InvestigateEmulatorIssue, OptimizePerformance, DeepReverseEngineering }

public sealed record BinaryAddress(AddressSpace AddressSpace, string Value);
public sealed record Evidence(string Source, string Detail, int? LineNumber = null);
public sealed record Provenance(string Source, string? Author, DateTimeOffset Date, string? KytyVersion, string? GameVersion, string? Hardware, VerificationStatus Status);
public sealed record BinarySection(string Name, BinaryAddress Offset, BinaryAddress Address, long Size);
public sealed record BinarySegment(string Type, BinaryAddress Offset, BinaryAddress Address, long FileSize, long MemorySize);

public sealed record GameFingerprint
{
    public int SchemaVersion { get; init; } = 1;
    public string? GameFamily { get; init; }
    public string? TitleId { get; init; }
    public string[] TitleIds { get; init; } = [];
    public string? TitleName { get; init; }
    public string? Region { get; init; }
    public string? GameVersion { get; init; }
    public string? BuildVersion { get; init; }
    public string? Engine { get; init; }
    public string? EngineVersion { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Sha256 { get; init; }
    public long FileSize { get; init; }
    public string? Architecture { get; init; }
    public BinaryAddress? EntryPoint { get; init; }
    public string? ElfType { get; init; }
    public BinarySection[] Sections { get; init; } = [];
    public BinarySegment[] Segments { get; init; } = [];
    public string[] Modules { get; init; } = [];
    public Evidence[] MetadataSources { get; init; } = [];
    public double Confidence { get; init; }
    public AnalysisStatus Status { get; init; } = AnalysisStatus.Partial;
    public string[] Limitations { get; init; } = [];
}

public sealed record GameFamily
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string[] TitleIds { get; init; } = [];
    public string? Engine { get; init; }
    public required Provenance Provenance { get; init; }
}

public sealed record KytyCapability(string Name, string[] Arguments, Evidence Evidence);
public sealed record KytyFingerprint
{
    public int SchemaVersion { get; init; } = 1;
    public required string ExecutablePath { get; init; }
    public required string Sha256 { get; init; }
    public long FileSize { get; init; }
    public string? Version { get; init; }
    public string? Architecture { get; init; }
    public KytyCapability[] Capabilities { get; init; } = [];
    public required string CapabilityStatus { get; init; }
    public Evidence[] Evidence { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

public sealed record ErrorEvent
{
    public int SchemaVersion { get; init; } = 1;
    public required string EventType { get; init; }
    public Severity Severity { get; init; }
    public ErrorCategory Category { get; init; } = ErrorCategory.Unknown;
    public required string Subsystem { get; init; }
    public required string Message { get; init; }
    public string? Opcode { get; init; }
    public BinaryAddress? ProgramCounter { get; init; }
    public string? SourceFile { get; init; }
    public int? SourceLine { get; init; }
    public required int LineNumber { get; init; }
    public required string RawLine { get; init; }
}

public sealed record ParsedLog
{
    public int SchemaVersion { get; init; } = 1;
    public required string SourcePath { get; init; }
    public required string Sha256 { get; init; }
    public required string RawText { get; init; }
    public string? KytyVersion { get; init; }
    public string? TitleId { get; init; }
    public Dictionary<string, string> Hardware { get; init; } = [];
    public ErrorEvent[] Events { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

// Versioned diagnosis and forward-looking patch/prompt contracts.
public sealed record RootCause
{
    public int SchemaVersion { get; init; } = 1;
    public required string Layer { get; init; }
    public string[] Facts { get; init; } = [];
    public Evidence[] Evidence { get; init; } = [];
    public string[] Hypotheses { get; init; } = [];
    public double Confidence { get; init; }
    public required string Recommendation { get; init; }
}

public sealed record PatchFamily
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string GameFamilyId { get; init; }
    public required string LogicalFix { get; init; }
    public string[] ImplementationIds { get; init; } = [];
    public required Provenance Provenance { get; init; }
}

public sealed record PatchProposal
{
    public int SchemaVersion { get; init; } = 1;
    public required string PatchId { get; init; }
    public required string PatchFamily { get; init; }
    public required string GameFamily { get; init; }
    public string[] TitleIds { get; init; } = [];
    public string[] GameVersions { get; init; } = [];
    public string[] KytyVersions { get; init; } = [];
    public required string TargetFile { get; init; }
    public required string Sha256 { get; init; }
    public required string Signature { get; init; }
    public required string OriginalBytes { get; init; }
    public required string ReplacementBytes { get; init; }
    public required BinaryAddress Location { get; init; }
    public int ExpectedMatches { get; init; } = 1;
    public required string Reason { get; init; }
    public required Provenance Provenance { get; init; }
    public string[] VerificationRules { get; init; } = [];
    public required string Risk { get; init; }
}

public sealed record AIPrompt
{
    public int SchemaVersion { get; init; } = 1;
    public PromptMode Mode { get; init; }
    public required string Text { get; init; }
    public double Readiness { get; init; }
    public string[] MissingEvidence { get; init; } = [];
    public Evidence[] Evidence { get; init; } = [];
}

public sealed record VerificationResult
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string GameFamilyId { get; init; }
    public required string BinarySha256 { get; init; }
    public required string KytySha256 { get; init; }
    public string? PatchId { get; init; }
    public required string Scenario { get; init; }
    public int Level { get; init; }
    public TestOutcome Outcome { get; init; } = TestOutcome.Inconclusive;
    public string? BaselineLog { get; init; }
    public string? CandidateLog { get; init; }
    public Dictionary<string, double> Metrics { get; init; } = [];
    public required Provenance Provenance { get; init; }
}

public sealed record ErrorSignature(int SchemaVersion, string Id, string[] ContainsAll, ErrorCategory Category, string Hypothesis, double Confidence, Provenance Provenance);
public sealed record FamilyRelationship(int SchemaVersion, string LeftBuild, string RightBuild, RelationshipStrength Strength, Evidence[] Evidence, double Confidence, Provenance Provenance);
public sealed record AnalysisReport(int SchemaVersion, GameFingerprint Game, KytyFingerprint? Kyty, ParsedLog? Log, string[] Limitations);
