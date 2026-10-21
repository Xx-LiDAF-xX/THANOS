using System.Globalization;
using System.Text;
using System.Text.Json;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class PatchImportService : IPatchImportService
{
    private static readonly HashSet<string> PatchProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion", "patchId", "patchFamily", "gameFamily", "titleIds", "gameVersions", "kytyVersions", "targetFile", "sha256", "signature",
        "originalBytes", "replacementBytes", "location", "expectedMatches", "reason", "provenance", "verificationRules", "risk"
    };
    private static readonly HashSet<string> AddressProperties = new(StringComparer.Ordinal) { "addressSpace", "value" };
    private static readonly HashSet<string> ProvenanceProperties = new(StringComparer.Ordinal) { "source", "author", "date", "kytyVersion", "gameVersion", "hardware", "status" };

    public async Task<PatchImportResult> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) { throw new FileNotFoundException("Patch input was not found.", fullPath); }
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is ".ips" or ".bps" or ".xdelta" or ".vcdiff" or ".patchbin")
        {
            return Inspect("binary-patch", "Binary patch bytes were identified. No embedded program or patch was executed.", "Binary formats cannot be normalized without an independently supplied target hash, original bytes, address space and provenance.");
        }

        string text;
        try { text = await File.ReadAllTextAsync(fullPath, new UTF8Encoding(false, true), cancellationToken); }
        catch (DecoderFallbackException)
        {
            return Inspect("binary-or-non-utf8", "The input is not strict UTF-8 and was not interpreted as code or patch instructions.", "Provide a versioned patch JSON proposal with exact hashes and bytes.");
        }

        var trimmed = text.Trim();
        var detected = "patch-json";
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            var fenced = JsonFence(text);
            if (fenced is not null) { trimmed = fenced; detected = "markdown-json"; }
            else if (IsDiff(text, extension)) { return Inspect("source-diff", "A source/unified diff was identified and retained for manual inspection. It was not applied.", "A source diff has no validated target executable hash, file offset or original-byte proof and cannot become an actionable binary PatchProposal automatically."); }
            else if (IsScript(extension, text)) { return Inspect("script", "Source code or a script was identified and retained for manual inspection. It was not compiled or executed.", "Script intent is not treated as validated bytes. Supply a versioned patch JSON proposal after independent review."); }
            else { return Reject("unknown", "No single patch JSON object or supported inspect-only format was found."); }
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            ValidateShape(document.RootElement);
            var patch = document.RootElement.Deserialize<PatchProposal>(JsonData.Options) ?? throw new InvalidDataException("Patch JSON cannot be null.");
            patch = Normalize(patch);
            return new PatchImportResult
            {
                Status = PatchImportStatus.Normalized,
                DetectedFormat = detected,
                Patch = patch,
                Findings = ["Versioned PatchProposal JSON normalized without executing supplied content.", "Provenance remains unverified until later real-world verification."],
                Limitations = ["Normalization establishes structure only. Target identity, ELF mapping, signature, original bytes and modification range still require binary validation."]
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or OverflowException)
        {
            return Reject(detected, ex.Message);
        }
    }

    public static PatchProposal Normalize(PatchProposal patch)
    {
        if (patch.SchemaVersion != 1) { throw new InvalidDataException("Unsupported patch schema version."); }
        Required(patch.PatchId, "patchId"); Required(patch.PatchFamily, "patchFamily"); Required(patch.GameFamily, "gameFamily");
        Required(patch.Reason, "reason"); Required(patch.Risk, "risk");
        var target = NormalizeTarget(patch.TargetFile);
        KnowledgeValidation.Hash(patch.Sha256);
        var signature = ParsePattern(patch.Signature, allowWildcards: true, "signature");
        var original = ParsePattern(patch.OriginalBytes, allowWildcards: false, "originalBytes");
        var replacement = ParsePattern(patch.ReplacementBytes, allowWildcards: false, "replacementBytes");
        if (original.Length == 0 || original.Length != replacement.Length) { throw new InvalidDataException("Original and replacement byte sequences must be nonempty and have equal length."); }
        if (original.SequenceEqual(replacement)) { throw new InvalidDataException("Replacement bytes must differ from original bytes."); }
        if (signature.Length < original.Length) { throw new InvalidDataException("Signature cannot be shorter than the modification."); }
        if (patch.ExpectedMatches < 1) { throw new InvalidDataException("expectedMatches must be at least one."); }
        if (!Enum.IsDefined(patch.Location.AddressSpace)) { throw new InvalidDataException("Unrecognized patch address space."); }
        var location = AddressMapper.Parse(patch.Location);
        if (patch.Provenance is null || patch.Provenance.Status != VerificationStatus.Unverified) { throw new InvalidDataException("Imported AI patch provenance must remain unverified."); }
        Required(patch.Provenance.Source, "provenance.source");
        if (!patch.Provenance.Source.Trim().StartsWith("external-ai", StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("Imported AI patch provenance source must begin with external-ai."); }
        if (patch.Provenance.Date == default) { throw new InvalidDataException("Patch provenance requires a date."); }
        var titleIds = Strings(patch.TitleIds, "titleIds");
        var gameVersions = Strings(patch.GameVersions, "gameVersions");
        var kytyVersions = Strings(patch.KytyVersions, "kytyVersions");
        var rules = Strings(patch.VerificationRules, "verificationRules");
        return patch with
        {
            PatchId = patch.PatchId.Trim(),
            PatchFamily = patch.PatchFamily.Trim(),
            GameFamily = patch.GameFamily.Trim(),
            TitleIds = titleIds,
            GameVersions = gameVersions,
            KytyVersions = kytyVersions,
            TargetFile = target,
            Signature = Format(signature),
            OriginalBytes = Format(original),
            ReplacementBytes = Format(replacement),
            Location = new BinaryAddress(patch.Location.AddressSpace, $"0x{location:x}"),
            Reason = patch.Reason.Trim(),
            VerificationRules = rules,
            Risk = patch.Risk.Trim(),
            Provenance = patch.Provenance with { Source = patch.Provenance.Source.Trim() }
        };
    }

    internal static byte?[] ParsePattern(string value, bool allowWildcards, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) { throw new InvalidDataException($"{name} cannot be empty."); }
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new byte?[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (allowWildcards && tokens[i] == "??") { continue; }
            if (tokens[i].Length != 2 || !byte.TryParse(tokens[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed)) { throw new InvalidDataException($"{name} must contain two-digit hex bytes separated by spaces{(allowWildcards ? " or ?? wildcards" : "")}."); }
            result[i] = parsed;
        }
        return result;
    }

    internal static byte[] Bytes(string value, string name) => ParsePattern(value, false, name).Select(b => b!.Value).ToArray();

    private static void ValidateShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Patch JSON must be one object."); }
        ExactObject(root, PatchProperties, "patch");
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number || !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1) { throw new InvalidDataException("Unsupported patch schema version."); }
        ExactObject(root.GetProperty("location"), AddressProperties, "location");
        ExactObject(root.GetProperty("provenance"), ProvenanceProperties, "provenance");
        DuplicateCheck(root);
    }

    private static void ExactObject(JsonElement element, HashSet<string> expected, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) { throw new InvalidDataException($"{name} must be an object."); }
        var actual = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Except(actual).FirstOrDefault();
        var extra = actual.Except(expected).FirstOrDefault();
        if (missing is not null) { throw new InvalidDataException($"Missing patch property: {name}.{missing}."); }
        if (extra is not null) { throw new InvalidDataException($"Unknown patch property: {name}.{extra}."); }
    }

    private static void DuplicateCheck(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate patch JSON properties are not accepted."); }
                DuplicateCheck(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) { foreach (var item in element.EnumerateArray()) { DuplicateCheck(item); } }
    }

    private static string NormalizeTarget(string value)
    {
        Required(value, "targetFile");
        var normalized = value.Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/');
        if (Path.IsPathRooted(value) || value.Contains(':') || parts.Length == 0 || parts.Any(p => p is "" or "." or ".." || p.IndexOfAny(['*', '?', '\0']) >= 0 || p.Any(char.IsControl))) { throw new InvalidDataException("targetFile must be a safe relative path without traversal, wildcards or drive/URI syntax."); }
        return string.Join('/', parts);
    }

    private static string[] Strings(string[]? values, string name)
    {
        if (values is null || values.Any(string.IsNullOrWhiteSpace)) { throw new InvalidDataException($"{name} must be a non-null array of nonempty strings."); }
        var trimmed = values.Select(v => v.Trim()).ToArray();
        if (trimmed.Distinct(StringComparer.Ordinal).Count() != trimmed.Length) { throw new InvalidDataException($"{name} contains duplicates."); }
        return trimmed;
    }

    private static string Format(IEnumerable<byte?> bytes) => string.Join(' ', bytes.Select(b => b.HasValue ? b.Value.ToString("x2", CultureInfo.InvariantCulture) : "??"));
    private static void Required(string? value, string name) { if (string.IsNullOrWhiteSpace(value)) { throw new InvalidDataException($"{name} is required."); } }
    private static bool IsDiff(string text, string extension) => extension is ".diff" or ".patch" || text.StartsWith("diff --git ", StringComparison.Ordinal) || text.Contains("\n--- ", StringComparison.Ordinal) && text.Contains("\n+++ ", StringComparison.Ordinal);
    private static bool IsScript(string extension, string text) => extension is ".py" or ".ps1" or ".sh" or ".cs" or ".cpp" or ".cc" or ".c" or ".js" or ".cmd" or ".bat" || text.StartsWith("#!", StringComparison.Ordinal);

    private static string? JsonFence(string text)
    {
        string? found = null;
        using var reader = new StringReader(text);
        StringBuilder? candidate = null;
        while (reader.ReadLine() is { } line)
        {
            if (candidate is null && line.Trim().Equals("```json", StringComparison.OrdinalIgnoreCase)) { candidate = new StringBuilder(); continue; }
            if (candidate is not null && line.Trim() == "```")
            {
                if (found is not null) { return null; }
                found = candidate.ToString().Trim(); candidate = null; continue;
            }
            candidate?.AppendLine(line);
        }
        return candidate is null ? found : null;
    }

    private static PatchImportResult Inspect(string format, string finding, string limitation) => new()
    {
        Status = PatchImportStatus.InspectionRequired,
        DetectedFormat = format,
        Findings = [finding],
        Limitations = [limitation, "THANOS never executes imported AI code automatically."]
    };

    private static PatchImportResult Reject(string format, string reason) => new()
    {
        Status = PatchImportStatus.Rejected,
        DetectedFormat = format,
        Findings = ["PATCH REJECTED"],
        Limitations = [reason, "No imported content was executed or applied."]
    };
}
