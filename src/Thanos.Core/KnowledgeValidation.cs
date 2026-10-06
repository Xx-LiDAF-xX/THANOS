namespace Thanos.Core;

public static class KnowledgeValidation
{
    public const int MaximumRecords = 10000;
    public static void Validate(KnowledgeBundle bundle)
    {
        if (bundle.SchemaVersion != 1) { throw new InvalidDataException("Unsupported knowledge schema version."); }
        if (bundle.Families is null || bundle.Bindings is null || bundle.PatchFamilies is null || bundle.Entries is null || bundle.ErrorSignatures is null || bundle.Relationships is null) { throw new InvalidDataException("Knowledge collections cannot be null."); }
        if (bundle.Families.Any(f => f is null) || bundle.Bindings.Any(b => b is null) || bundle.PatchFamilies.Any(p => p is null) || bundle.Entries.Any(e => e is null) || bundle.ErrorSignatures.Any(s => s is null) || bundle.Relationships.Any(r => r is null)) { throw new InvalidDataException("Knowledge records cannot be null."); }
        var count = (long)bundle.Families.Length + bundle.Bindings.Length + bundle.PatchFamilies.Length + bundle.Entries.Length + bundle.ErrorSignatures.Length + bundle.Relationships.Length;
        if (count > MaximumRecords) { throw new InvalidDataException("Knowledge bundle exceeds 10000 records."); }
        Unique(bundle.Families.Select(f => f.Id));
        Unique(bundle.PatchFamilies.Select(p => p.Id));
        Unique(bundle.Entries.Select(e => e.Id));
        Unique(bundle.Bindings.Select(b => b.ExecutableSha256));
        foreach (var family in bundle.Families)
        {
            Version(family.SchemaVersion); Text(family.Name); Provenance(family.Provenance);
            if (family.TitleIds is null) { throw new InvalidDataException("Title IDs cannot be null."); }
        }
        var families = bundle.Families.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var binding in bundle.Bindings) { Hash(binding.ExecutableSha256); Family(binding.GameFamilyId, families); Provenance(binding.Provenance); }
        foreach (var patch in bundle.PatchFamilies)
        {
            Version(patch.SchemaVersion); Family(patch.GameFamilyId, families); Text(patch.LogicalFix); Provenance(patch.Provenance);
            if (patch.ImplementationIds is null) { throw new InvalidDataException("Implementation IDs cannot be null."); }
        }
        foreach (var entry in bundle.Entries)
        {
            Version(entry.SchemaVersion); Text(entry.Summary); Provenance(entry.Provenance);
            if (entry.Kind is not ("issue" or "fix" or "compatibility")) { throw new InvalidDataException("Unsupported knowledge entry kind."); }
            if (entry.GameFamilyId is not null) { Family(entry.GameFamilyId, families); }
            if (entry.ExecutableHashes is null || entry.SignatureIds is null) { throw new InvalidDataException("Knowledge selectors cannot be null."); }
            foreach (var hash in entry.ExecutableHashes) { Hash(hash); }
            foreach (var id in entry.SignatureIds) { Text(id); }
            if (entry.ExecutableHashes.Length == 0 && entry.SignatureIds.Length == 0 && entry.GameFamilyId is null) { throw new InvalidDataException("Knowledge entry must have an explicit hash, family or signature selector."); }
        }
        if (bundle.ErrorSignatures.Length > 512) { throw new InvalidDataException("At most 512 imported error signatures are supported."); }
        ValidateSignatures(bundle.ErrorSignatures);
        if (bundle.ErrorSignatures.Any(s => DiagnosisEngine.BuiltInSignatures.Any(b => b.Id == s.Id))) { throw new InvalidDataException("Imported signatures cannot replace reserved built-in signature IDs."); }
        if (bundle.Relationships.Select(r => (r.LeftBuild, r.RightBuild)).Distinct().Count() != bundle.Relationships.Length) { throw new InvalidDataException("Duplicate relationship pair."); }
        foreach (var relationship in bundle.Relationships)
        {
            Version(relationship.SchemaVersion); Text(relationship.LeftBuild); Text(relationship.RightBuild); Confidence(relationship.Confidence); Provenance(relationship.Provenance);
            if (!Enum.IsDefined(relationship.Strength) || relationship.Evidence is null || relationship.Evidence.Length > MaximumRecords) { throw new InvalidDataException("Invalid relationship evidence/strength."); }
            foreach (var item in relationship.Evidence) { if (item is null) { throw new InvalidDataException("Evidence cannot be null."); } Text(item.Source); Text(item.Detail); if (item.LineNumber <= 0) { throw new InvalidDataException("Invalid evidence line number."); } }
            if (relationship.Strength != RelationshipStrength.Unknown && relationship.Evidence.Length == 0) { throw new InvalidDataException("Relationship claims require evidence."); }
        }
    }
    public static void ValidateSignatures(IReadOnlyList<ErrorSignature> signatures)
    {
        if (signatures.Count > 1024) { throw new InvalidDataException("At most 1024 combined error signatures are supported."); }
        if (signatures.Any(s => s is null)) { throw new InvalidDataException("Signatures cannot be null."); }
        Unique(signatures.Select(s => s.Id));
        foreach (var signature in signatures)
        {
            Version(signature.SchemaVersion); Text(signature.Hypothesis); Confidence(signature.Confidence); Provenance(signature.Provenance);
            if (!Enum.IsDefined(signature.Category) || signature.ContainsAll is null || signature.ContainsAll.Length is < 1 or > 16) { throw new InvalidDataException("Invalid signature category or token count."); }
            foreach (var token in signature.ContainsAll) { Text(token); }
        }
    }
    public static bool IsTrusted(Provenance provenance) => provenance.Status is VerificationStatus.Confirmed or VerificationStatus.ToolVerified;
    public static void Hash(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(c => !((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9')))) { throw new InvalidDataException("Expected lowercase SHA-256."); }
    }
    private static void Unique(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids) { Text(id); if (!seen.Add(id)) { throw new InvalidDataException("Duplicate knowledge identifier."); } }
    }
    private static void Family(string id, HashSet<string> families) { Text(id); if (!families.Contains(id)) { throw new InvalidDataException("Referenced game family is absent from the bundle."); } }
    private static void Version(int version) { if (version != 1) { throw new InvalidDataException("Unsupported record schema version."); } }
    private static void Text(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) { throw new InvalidDataException("Required knowledge text must be 1–4096 characters."); } }
    private static void Confidence(double value) { if (!double.IsFinite(value) || value is < 0 or > 1) { throw new InvalidDataException("Confidence must be between zero and one."); } }
    private static void Provenance(Provenance provenance)
    {
        if (provenance is null) { throw new InvalidDataException("Knowledge requires provenance."); }
        Text(provenance.Source);
        if (provenance.Date == default || !Enum.IsDefined(provenance.Status)) { throw new InvalidDataException("Provenance requires a date and recognized status."); }
    }
}
