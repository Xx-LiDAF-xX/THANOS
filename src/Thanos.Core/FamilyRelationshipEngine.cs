namespace Thanos.Core;

public sealed class FamilyRelationshipEngine : IFamilyRelationshipEngine
{
    public FamilyRelationship Compare(BinaryInspection left, BinaryInspection right, IReadOnlyList<FamilyBinding>? bindings = null)
    {
        var evidence = new List<Evidence>();
        var strength = RelationshipStrength.Unknown;
        var confidence = 0.0;
        var leftId = left.Fingerprint.Sha256 ?? left.Fingerprint.ExecutablePath ?? "UNKNOWN";
        var rightId = right.Fingerprint.Sha256 ?? right.Fingerprint.ExecutablePath ?? "UNKNOWN";
        var provenance = new Provenance("THANOS binary relationship policy v1", null, DateTimeOffset.UtcNow, null, null, null, VerificationStatus.Unverified);
        // A matching opaque/protected container is not evidence of matching inspectable code.
        if (left.Status is BinaryInspectionStatus.Malformed or BinaryInspectionStatus.Unsupported || right.Status is BinaryInspectionStatus.Malformed or BinaryInspectionStatus.Unsupported
            || left.CodeSignatures.Length == 0 || right.CodeSignatures.Length == 0 || left.Fingerprint.Architecture is null || left.Fingerprint.Architecture != right.Fingerprint.Architecture)
        {
            evidence.Add(new Evidence("binary inspection", "Incomplete/unsupported inspections or incompatible architectures; relationship is UNKNOWN."));
            return new FamilyRelationship(1, leftId, rightId, strength, evidence.ToArray(), confidence, provenance);
        }
        var leftBinding = bindings?.SingleOrDefault(b => b.ExecutableSha256 == left.Fingerprint.Sha256);
        var rightBinding = bindings?.SingleOrDefault(b => b.ExecutableSha256 == right.Fingerprint.Sha256);
        if (leftBinding is not null && rightBinding is not null && KnowledgeValidation.IsTrusted(leftBinding.Provenance) && KnowledgeValidation.IsTrusted(rightBinding.Provenance))
        {
            if (leftBinding.GameFamilyId == rightBinding.GameFamilyId)
            {
                evidence.Add(new Evidence(leftBinding.Provenance.Source, $"Explicit trusted binding of {leftId} to family {leftBinding.GameFamilyId}."));
                evidence.Add(new Evidence(rightBinding.Provenance.Source, $"Explicit trusted binding of {rightId} to family {rightBinding.GameFamilyId}."));
                return new FamilyRelationship(1, leftId, rightId, RelationshipStrength.ExactFamily, evidence.ToArray(), 1.0, provenance);
            }
            evidence.Add(new Evidence("family bindings", "Trusted records assign different families; automatic similarity cannot override these bindings."));
            return new FamilyRelationship(1, leftId, rightId, RelationshipStrength.Unknown, evidence.ToArray(), 0, provenance);
        }
        if (left.Fingerprint.Sha256 is not null && left.Fingerprint.Sha256 == right.Fingerprint.Sha256 && left.CodeSignatures.Length > 0)
        {
            strength = RelationshipStrength.Strong; confidence = 0.98;
            evidence.Add(new Evidence("SHA-256", "Exact accessible ELF bytes including code match. Game-family identity still requires provenance."));
        }
        else
        {
            if (!left.CodeInventoryComplete || !right.CodeInventoryComplete)
            {
                evidence.Add(new Evidence("code inventory", "Code hashes are incomplete; coverage cannot establish a relationship between different binaries."));
                return new FamilyRelationship(1, leftId, rightId, RelationshipStrength.Unknown, evidence.ToArray(), 0, provenance);
            }
            var leftCode = left.CodeSignatures.Where(s => s.Kind == "section" && s.Size >= 64).Select(s => (s.Sha256, s.Size)).ToHashSet();
            var rightCode = right.CodeSignatures.Where(s => s.Kind == "section" && s.Size >= 64).Select(s => (s.Sha256, s.Size)).ToHashSet();
            var commonCode = leftCode.Intersect(rightCode).ToArray();
            var leftBytes = leftCode.Sum(s => s.Size);
            var rightBytes = rightCode.Sum(s => s.Size);
            var commonBytes = commonCode.Sum(s => s.Size);
            var coverage = Math.Max(leftBytes, rightBytes) == 0 ? 0 : (double)commonBytes / Math.Max(leftBytes, rightBytes);
            if (coverage >= 0.9)
            {
                strength = RelationshipStrength.Strong; confidence = 0.85;
                evidence.Add(new Evidence("executable section hashes", $"Exact matching executable sections cover {coverage:P1} of the larger observed code inventory. Common libraries can also produce this similarity."));
            }
            var leftSymbols = left.Symbols.Where(s => s.IsExport && s.Type == "function").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            var rightSymbols = right.Symbols.Where(s => s.IsExport && s.Type == "function").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            var commonSymbols = leftSymbols.Intersect(rightSymbols).Count();
            var symbolUnion = leftSymbols.Union(rightSymbols).Count();
            if (strength == RelationshipStrength.Unknown && commonSymbols >= 8 && symbolUnion > 0 && (double)commonSymbols / symbolUnion >= 0.8 && coverage >= 0.3)
            {
                strength = RelationshipStrength.Probable; confidence = 0.6;
                evidence.Add(new Evidence("code and exports", $"{commonSymbols} exported functions overlap alongside {coverage:P1} exact executable-section coverage; this is a relationship hypothesis."));
            }
            if (strength == RelationshipStrength.Unknown && commonCode.Length > 0)
            {
                strength = RelationshipStrength.Weak; confidence = 0.25;
                evidence.Add(new Evidence("executable section hashes", $"{commonCode.Length} code section hashes match; shared engine/library code is an alternative explanation."));
            }
        }
        if (evidence.Count == 0) { evidence.Add(new Evidence("binary comparison", "No sufficiently specific code evidence. Title IDs, filenames and generic strings do not establish family identity.")); }
        return new FamilyRelationship(1, leftId, rightId, strength, evidence.ToArray(), confidence, provenance);
    }
}
