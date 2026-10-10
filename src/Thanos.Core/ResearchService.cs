namespace Thanos.Core;

public sealed class ResearchService(IAnalysisService analysis, IBinaryInspectionService binaries, IDiagnosisEngine diagnosis) : IResearchService
{
    public async Task<ResearchReport> ResearchAsync(string game, string? kyty, string? log, KnowledgeBundle? knowledge = null, string? relatedBuild = null, CancellationToken cancellationToken = default)
    {
        knowledge ??= new KnowledgeBundle();
        KnowledgeValidation.Validate(knowledge);
        var evidence = await analysis.AnalyzeAsync(game, kyty, log, cancellationToken);
        var binary = evidence.Game.ExecutablePath is null ? null : await binaries.InspectAsync(evidence.Game.ExecutablePath, cancellationToken);
        if (binary is not null && binary.Fingerprint.Sha256 != evidence.Game.Sha256) { throw new InvalidDataException("Game file changed during evidence collection. Retry with a stable input."); }
        if (binary is not null)
        {
            binary = binary with { Fingerprint = binary.Fingerprint with { Modules = evidence.Game.Modules, MetadataSources = evidence.Game.MetadataSources, Limitations = evidence.Game.Limitations.Concat(binary.Fingerprint.Limitations).Distinct().ToArray() } };
            evidence = evidence with { Game = binary.Fingerprint, Limitations = ["Research collects evidence and hypotheses. Prompts may be generated separately; patches and emulator execution are not implemented."] };
        }
        else { evidence = evidence with { Limitations = ["No unambiguous binary available. Log-based diagnosis and evidence prompts remain possible; patches and execution are not implemented."] }; }
        var signatures = DiagnosisEngine.BuiltInSignatures.Concat(knowledge.ErrorSignatures).ToArray();
        var findings = diagnosis.Diagnose(evidence.Log, signatures);
        var familyIds = knowledge.Bindings.Where(b => b.ExecutableSha256 == evidence.Game.Sha256).Select(b => b.GameFamilyId).ToHashSet(StringComparer.Ordinal);
        var families = knowledge.Families.Where(f => familyIds.Contains(f.Id)).ToArray();
        var matchedSignatures = findings.Matches.Select(m => m.SignatureId).ToHashSet(StringComparer.Ordinal);
        var issues = knowledge.Entries.Where(e => (e.ExecutableHashes.Length == 0 || e.ExecutableHashes.Contains(evidence.Game.Sha256, StringComparer.Ordinal))
            && (e.GameFamilyId is null || familyIds.Contains(e.GameFamilyId))
            && (e.SignatureIds.Length == 0 || e.SignatureIds.Any(matchedSignatures.Contains))).ToArray();
        var limitations = new List<string> { "Root causes and family relationships are candidates, not verified game compatibility. Imported claims retain their original verification status.", "Knowledge selector matches are historical claims. Kyty/game version and hardware applicability require separate verification; no fix is declared valid for this environment.", "Known patch families are logical knowledge records; implementations are not applied or tested." };
        var trustedFamilyIds = knowledge.Bindings.Where(b => b.ExecutableSha256 == evidence.Game.Sha256 && KnowledgeValidation.IsTrusted(b.Provenance)).Select(b => b.GameFamilyId).Distinct().ToArray();
        if (trustedFamilyIds.Length == 1 && families.Single(f => f.Id == trustedFamilyIds[0]) is { } family && KnowledgeValidation.IsTrusted(family.Provenance))
        {
            evidence = evidence with
            {
                Game = evidence.Game with
                {
                    GameFamily = family.Id,
                    Confidence = 1,
                    MetadataSources = [.. evidence.Game.MetadataSources, new Evidence(family.Provenance.Source, "Game family assigned by explicit trusted executable-hash binding.")]
                }
            };
        }
        else if (familyIds.Count > 0) { limitations.Add("Unverified family bindings are related claims only; the game fingerprint remains unassigned."); }
        FamilyRelationship[] relationships = [];
        if (relatedBuild is not null && binary is not null)
        {
            var related = await binaries.InspectAsync(relatedBuild, cancellationToken);
            relationships = [new FamilyRelationshipEngine().Compare(binary, related, knowledge.Bindings)];
        }
        else if (relatedBuild is not null) { limitations.Add("Related-build comparison needs an unambiguous primary executable."); }
        return new ResearchReport
        {
            Evidence = evidence,
            Binary = binary,
            Diagnosis = findings,
            RelatedFamilies = families,
            KnownIssues = issues,
            RelatedBuilds = relationships,
            PatchFamilies = knowledge.PatchFamilies.Where(p => familyIds.Contains(p.GameFamilyId)).ToArray(),
            Limitations = limitations.ToArray()
        };
    }
}
