namespace Thanos.Core;

public interface IBinaryInspectionService
{
    Task<BinaryInspection> InspectAsync(string path, CancellationToken cancellationToken = default);
}
public interface IAddressMapper
{
    AddressMappingResult Map(BinaryInspection binary, BinaryAddress address, AddressSpace destination, ulong? loadBias = null);
}
public interface IDiagnosisEngine
{
    DiagnosisReport Diagnose(ParsedLog? log, IReadOnlyList<ErrorSignature>? signatures = null);
}
public interface IFamilyRelationshipEngine
{
    FamilyRelationship Compare(BinaryInspection left, BinaryInspection right, IReadOnlyList<FamilyBinding>? bindings = null);
}
public interface IResearchService
{
    Task<ResearchReport> ResearchAsync(string game, string? kyty, string? log, KnowledgeBundle? knowledge = null, string? relatedBuild = null, CancellationToken cancellationToken = default);
}
public interface IResearchKnowledgeStore : IKnowledgeStore
{
    Task ImportKnowledgeAsync(KnowledgeBundle bundle, CancellationToken cancellationToken = default);
    Task<KnowledgeBundle> ExportKnowledgeAsync(CancellationToken cancellationToken = default);
    Task SaveResearchAsync(string id, ResearchReport report, CancellationToken cancellationToken = default);
    Task<ResearchReport?> GetResearchAsync(string id, CancellationToken cancellationToken = default);
    Task SaveVerificationAsync(string id, VerificationResult result, CancellationToken cancellationToken = default);
    Task<VerificationResult?> GetVerificationAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VerificationResult>> ListVerificationsAsync(string? binarySha256 = null, CancellationToken cancellationToken = default);
}
public interface IBinaryDiffer
{
    Task<BinaryDifference> CompareAsync(string original, string candidate, CancellationToken cancellationToken = default);
}
