namespace Thanos.Core;

public enum AnalysisStatus { Partial, Complete }
public enum AddressSpace { FileOffset, VirtualAddress, RuntimeAddress, RelativeAddress }
public enum Severity { Information, Warning, Error, Fatal }
public enum ErrorCategory { Boot, Elf, Loader, Module, Syscall, Memory, Thread, Timing, Filesystem, Graphics, Vulkan, Shader, Gpu, Driver, Audio, Video, Network, Input, Presentation, GamePatch, VersionMismatch, Performance, Environment, Unknown }
public enum VerificationStatus { Unverified, CommunityReported, Repeated, Confirmed, ToolVerified }
public enum RelationshipStrength { ExactFamily, Strong, Probable, Weak, Unknown }
public enum TestOutcome { Pass, Fail, Improved, Regressed, Inconclusive }
public enum PromptMode { Diagnose, FindExistingFix, WritePatch, PortPatch, CompareBuilds, InvestigateEmulatorIssue, OptimizePerformance, DeepReverseEngineering }
public enum PatchImportStatus { Normalized, InspectionRequired, Rejected }
public enum PatchValidationStatus { Valid, Rejected }
public enum PatchTransactionStatus { Applied, RolledBack }

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

public sealed record PatchCheck(string Name, bool Passed, bool Critical, string Detail, Evidence[] Evidence);

public sealed record PatchImportResult
{
    public int SchemaVersion { get; init; } = 1;
    public PatchImportStatus Status { get; init; }
    public required string DetectedFormat { get; init; }
    public PatchProposal? Patch { get; init; }
    public string[] Findings { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

public sealed record PatchValidationResult
{
    public int SchemaVersion { get; init; } = 1;
    public required string PatchId { get; init; }
    public PatchValidationStatus Status { get; init; }
    public string? TargetPath { get; init; }
    public string? TargetSha256 { get; init; }
    public BinaryAddress? FileOffset { get; init; }
    public PatchCheck[] Checks { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

public sealed record PatchTransaction
{
    public int SchemaVersion { get; init; } = 1;
    public required string TransactionId { get; init; }
    public required string PatchId { get; init; }
    public PatchTransactionStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required string OriginalRoot { get; init; }
    public required string TargetRelativePath { get; init; }
    public required string OriginalSha256 { get; init; }
    public required string BackupSha256 { get; init; }
    public required string CandidateSha256 { get; init; }
    public required string WorkingRoot { get; init; }
    public required string BackupRoot { get; init; }
    public required BinaryAddress FileOffset { get; init; }
    public long Length { get; init; }
    public string[] Limitations { get; init; } = [];
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

public enum ScenarioType
{
    Boot,
    Menu,
    Gameplay,
    CrashReproduction,
    Stability,
    Performance
}

public enum VerificationLevel
{
    None = 0,
    Boot = 1,
    Intro = 2,
    Menu = 3,
    InGame = 4,
    Playable = 5,
    Complete = 6,
    Perfect = 7
}

public sealed record ScenarioDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Name { get; init; }
    public ScenarioType Type { get; init; }
    public int TargetLevel { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public string[] RequiredPatterns { get; init; } = [];
    public string[] FailurePatterns { get; init; } = [];
    public TimeSpan? MinDuration { get; init; }
    public Dictionary<string, string> Environment { get; init; } = [];
    public string[] AdditionalArguments { get; init; } = [];

    public static string ToWireName(ScenarioType type) => type switch
    {
        ScenarioType.Boot => "BOOT",
        ScenarioType.Menu => "MENU",
        ScenarioType.Gameplay => "GAMEPLAY",
        ScenarioType.CrashReproduction => "CRASH REPRODUCTION",
        ScenarioType.Stability => "STABILITY",
        ScenarioType.Performance => "PERFORMANCE",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown scenario type.")
    };

    public static ScenarioType ParseWireName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "BOOT" => ScenarioType.Boot,
        "MENU" => ScenarioType.Menu,
        "GAMEPLAY" => ScenarioType.Gameplay,
        "CRASH REPRODUCTION" or "CRASH_REPRODUCTION" or "CRASHREPRODUCTION" => ScenarioType.CrashReproduction,
        "STABILITY" => ScenarioType.Stability,
        "PERFORMANCE" => ScenarioType.Performance,
        _ => throw new ArgumentException($"Unknown scenario wire name '{name}'.", nameof(name))
    };

    public static ScenarioDefinition CreateBuiltIn(ScenarioType type) => type switch
    {
        ScenarioType.Boot => new()
        {
            Name = "BOOT",
            Type = ScenarioType.Boot,
            TargetLevel = (int)VerificationLevel.Boot,
            Timeout = TimeSpan.FromSeconds(20),
            RequiredPatterns = ["runtimeLinker", "InitAll", "guest execution", "LoadSelf", "Mounting"],
            FailurePatterns = ["FATAL", "shader CFG build failed", "Unhandled exception", "Assertion failed"]
        },
        ScenarioType.Menu => new()
        {
            Name = "MENU",
            Type = ScenarioType.Menu,
            TargetLevel = (int)VerificationLevel.Menu,
            Timeout = TimeSpan.FromSeconds(35),
            RequiredPatterns = ["menu", "title", "ui", "controller", "dialog", "scene"],
            FailurePatterns = ["FATAL", "shader CFG build failed", "Unhandled exception", "Assertion failed"]
        },
        ScenarioType.Gameplay => new()
        {
            Name = "GAMEPLAY",
            Type = ScenarioType.Gameplay,
            TargetLevel = (int)VerificationLevel.InGame,
            Timeout = TimeSpan.FromSeconds(45),
            RequiredPatterns = ["gameplay", "world", "level", "player", "stage", "camera"],
            FailurePatterns = ["FATAL", "shader CFG build failed", "Unhandled exception", "Assertion failed"]
        },
        ScenarioType.CrashReproduction => new()
        {
            Name = "CRASH REPRODUCTION",
            Type = ScenarioType.CrashReproduction,
            TargetLevel = (int)VerificationLevel.Boot,
            Timeout = TimeSpan.FromSeconds(25),
            RequiredPatterns = ["guest execution", "runtimeLinker"],
            FailurePatterns = ["FATAL", "Unhandled exception", "Assertion failed"]
        },
        ScenarioType.Stability => new()
        {
            Name = "STABILITY",
            Type = ScenarioType.Stability,
            TargetLevel = (int)VerificationLevel.Playable,
            Timeout = TimeSpan.FromSeconds(60),
            MinDuration = TimeSpan.FromSeconds(15),
            RequiredPatterns = ["guest execution"],
            FailurePatterns = ["FATAL", "Unhandled exception", "Assertion failed", "panic"]
        },
        ScenarioType.Performance => new()
        {
            Name = "PERFORMANCE",
            Type = ScenarioType.Performance,
            TargetLevel = (int)VerificationLevel.Perfect,
            Timeout = TimeSpan.FromSeconds(60),
            MinDuration = TimeSpan.FromSeconds(20),
            RequiredPatterns = ["guest execution", "fps", "frame"],
            FailurePatterns = ["FATAL", "Unhandled exception", "Assertion failed", "drop"]
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown scenario type.")
    };
}

public sealed record ErrorSignature(int SchemaVersion, string Id, string[] ContainsAll, ErrorCategory Category, string Hypothesis, double Confidence, Provenance Provenance);
public sealed record FamilyRelationship(int SchemaVersion, string LeftBuild, string RightBuild, RelationshipStrength Strength, Evidence[] Evidence, double Confidence, Provenance Provenance);
public sealed record AnalysisReport(int SchemaVersion, GameFingerprint Game, KytyFingerprint? Kyty, ParsedLog? Log, string[] Limitations);
