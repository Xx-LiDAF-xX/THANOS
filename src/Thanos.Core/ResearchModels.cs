namespace Thanos.Core;

public enum BinaryInspectionStatus { Inspected, Unsupported, Malformed, Limited }
public sealed record SymbolInfo(string Name, BinaryAddress Address, long Size, string Binding, string Type, int SectionIndex, bool IsImport, bool IsExport);
public sealed record RelocationInfo(BinaryAddress Location, string Type, string? Symbol, long? Addend);
public sealed record BinaryString(BinaryAddress Location, string Value);
public sealed record CodeSignature(string Name, BinaryAddress Location, long Size, string Sha256, string Kind);
public sealed record LoadMapping(BinaryAddress Offset, BinaryAddress Address, long FileSize, long MemorySize, uint Flags);
public sealed record BinaryInspection
{
    public int SchemaVersion { get; init; } = 1;
    public required GameFingerprint Fingerprint { get; init; }
    public BinaryInspectionStatus Status { get; init; }
    public string? Format { get; init; }
    public string? ByteOrder { get; init; }
    public SymbolInfo[] Symbols { get; init; } = [];
    public string[] Imports { get; init; } = [];
    public string[] Exports { get; init; } = [];
    public RelocationInfo[] Relocations { get; init; } = [];
    public BinaryString[] Strings { get; init; } = [];
    public CodeSignature[] CodeSignatures { get; init; } = [];
    public bool CodeInventoryComplete { get; init; }
    public LoadMapping[] LoadMappings { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

public sealed record AddressMappingResult(int SchemaVersion, BinaryAddress Input, BinaryAddress? Output, string Status, Evidence[] Evidence);
public sealed record SignatureMatch(string SignatureId, int LineNumber, string Hypothesis, double Confidence, Provenance Provenance);
public sealed record CorrelationLink(int FromLine, int ToLine, string Relationship, double Confidence, Evidence[] Evidence);
public sealed record DiagnosisReport
{
    public int SchemaVersion { get; init; } = 1;
    public ErrorEvent? PrimaryFailure { get; init; }
    public ErrorEvent[] SecondaryFailures { get; init; } = [];
    public ErrorEvent[] IndependentFailures { get; init; } = [];
    public SignatureMatch[] Matches { get; init; } = [];
    public CorrelationLink[] Correlations { get; init; } = [];
    public RootCause[] Candidates { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

public sealed record KnowledgeEntry
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public string? GameFamilyId { get; init; }
    public required string Kind { get; init; }
    public required string Summary { get; init; }
    public string[] ExecutableHashes { get; init; } = [];
    public string[] SignatureIds { get; init; } = [];
    public required Provenance Provenance { get; init; }
}

// A binding refers to exact accessible file bytes, not merely a title ID.
public sealed record FamilyBinding(string ExecutableSha256, string GameFamilyId, Provenance Provenance);
public sealed record KnowledgeBundle
{
    public int SchemaVersion { get; init; } = 1;
    public GameFamily[] Families { get; init; } = [];
    public FamilyBinding[] Bindings { get; init; } = [];
    public PatchFamily[] PatchFamilies { get; init; } = [];
    public KnowledgeEntry[] Entries { get; init; } = [];
    public ErrorSignature[] ErrorSignatures { get; init; } = [];
    public FamilyRelationship[] Relationships { get; init; } = [];
}

public sealed record ResearchReport
{
    public int SchemaVersion { get; init; } = 1;
    public required AnalysisReport Evidence { get; init; }
    public BinaryInspection? Binary { get; init; }
    public required DiagnosisReport Diagnosis { get; init; }
    public GameFamily[] RelatedFamilies { get; init; } = [];
    public FamilyRelationship[] RelatedBuilds { get; init; } = [];
    public KnowledgeEntry[] KnownIssues { get; init; } = [];
    public PatchFamily[] PatchFamilies { get; init; } = [];
    public string[] Limitations { get; init; } = [];
}

public sealed record ChangedRange(BinaryAddress Offset, long OriginalSize, long CandidateSize, string OriginalBytes, string CandidateBytes, string BeforeContext, string AfterContext);
public sealed record BinaryDifference(int SchemaVersion, string OriginalSha256, string CandidateSha256, long ChangedBytes, ChangedRange[] Ranges, bool Truncated, string[] Limitations);
