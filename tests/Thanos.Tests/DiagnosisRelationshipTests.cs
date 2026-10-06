using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Tests;

public sealed class DiagnosisRelationshipTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thanos-research-" + Guid.NewGuid().ToString("N"));
    public DiagnosisRelationshipTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private async Task<ParsedLog> LogAsync(string text)
    {
        var path = Path.Combine(directory, "kyty.log"); await File.WriteAllTextAsync(path, text);
        return await new KytyLogParser(new LocalFileSystem(), new LocalPathService()).ParseAsync(path);
    }
    private async Task<BinaryInspection> BinaryAsync(byte[] data)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N")); await File.WriteAllBytesAsync(path, data);
        return await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(path);
    }

    [Fact]
    public async Task ShaderErrorPrecedesDownstreamGraphicsTerminationButNotUnrelatedAudio()
    {
        var parsed = await LogAsync("Shader fatal unsupported family=MIMG\nGraphics pipeline failed\nVulkan error VK_ERROR_DEVICE_LOST\nAudio error\nProcess terminated\n");
        var result = new DiagnosisEngine().Diagnose(parsed);
        Assert.Equal(1, result.PrimaryFailure!.LineNumber);
        Assert.Equal([2, 3, 5], result.SecondaryFailures.Select(e => e.LineNumber).ToArray());
        Assert.Equal(4, Assert.Single(result.IndependentFailures).LineNumber);
        Assert.Equal(3, result.Correlations.Length);
        var cause = Assert.Single(result.Candidates);
        Assert.Equal("KYTY EMULATOR / SHADER", cause.Layer);
        Assert.Contains("unsupported", cause.Facts[0]);
        Assert.Equal(1, cause.Evidence[0].LineNumber);
        Assert.True(cause.Confidence < 1);
        Assert.Contains("may", cause.Hypotheses[0]);
    }

    [Fact]
    public async Task EarliestLoaderFailureIsNotHiddenByLaterShaderFatal()
    {
        var result = new DiagnosisEngine().Diagnose(await LogAsync("Loader failed to load executable\nShader fatal unsupported instruction\n"));
        Assert.Equal(ErrorCategory.Loader, result.PrimaryFailure!.Category);
        Assert.Equal("LOADER", Assert.Single(result.Candidates).Layer);
        Assert.Equal(ErrorCategory.Shader, Assert.Single(result.IndependentFailures).Category);
    }

    [Fact]
    public async Task TerminationOnlyAndMissingLogsCannotEstablishRootCauseOrSuccess()
    {
        var engine = new DiagnosisEngine();
        foreach (var result in new[] { engine.Diagnose(null), engine.Diagnose(await LogAsync("Process terminated")), engine.Diagnose(await LogAsync("Initialization complete")) })
        {
            Assert.Equal("UNKNOWN", Assert.Single(result.Candidates).Layer);
            Assert.Equal(0, result.Candidates[0].Confidence);
            Assert.Empty(result.Candidates[0].Hypotheses);
        }
    }

    [Fact]
    public async Task TemporalProximityDoesNotLinkDistantErrors()
    {
        var result = new DiagnosisEngine().Diagnose(await LogAsync("Shader fatal unsupported\n" + string.Concat(Enumerable.Repeat("info\n", 26)) + "Graphics pipeline failed\n"));
        Assert.Empty(result.Correlations);
        Assert.Single(result.IndependentFailures);
    }

    [Fact]
    public async Task NegatedFailureCountsDoNotBecomeFailureCandidates()
    {
        var result = new DiagnosisEngine().Diagnose(await LogAsync("No shader errors\nNo unsupported instructions\n0 warnings\n"));
        Assert.Null(result.PrimaryFailure);
        Assert.Empty(result.Matches);
        Assert.Equal("UNKNOWN", result.Candidates[0].Layer);
    }

    [Fact]
    public async Task LiteralCustomSignaturesRetainUnverifiedProvenanceAndConfidenceCap()
    {
        var provenance = new Provenance("external community claim", "example", DateTimeOffset.UtcNow, null, null, null, VerificationStatus.CommunityReported);
        var signature = new ErrorSignature(1, "example-custom", ["custom [a-z]+"], ErrorCategory.Shader, "Community hypothesis", 1, provenance);
        var engine = new DiagnosisEngine();
        Assert.Empty(engine.Diagnose(await LogAsync("Shader error custom abc"), [signature]).Matches);
        var result = engine.Diagnose(await LogAsync("Shader error custom [a-z]+"), [signature]);
        Assert.Equal(VerificationStatus.CommunityReported, Assert.Single(result.Matches).Provenance.Status);
        Assert.Equal(0.7, result.Candidates[0].Confidence);
    }

    [Fact]
    public async Task FamilySimilarityUsesCodeNotSimilarTitleIdsOrCommonMetadataStrings()
    {
        var left = await BinaryAsync(ElfFixture.Create());
        var rightData = ElfFixture.Create(); rightData[256] ^= 0x7f;
        var right = await BinaryAsync(rightData);
        var engine = new FamilyRelationshipEngine();
        Assert.Equal(RelationshipStrength.Strong, engine.Compare(left, left).Strength);
        Assert.Equal(RelationshipStrength.Unknown, engine.Compare(left with { Fingerprint = left.Fingerprint with { TitleId = "CUSA00001" } }, right with { Fingerprint = right.Fingerprint with { TitleId = "CUSA00002" } }).Strength);
        var opaque = await BinaryAsync([1, 2, 3]);
        Assert.Equal(RelationshipStrength.Unknown, engine.Compare(opaque, opaque).Strength);
    }

    [Fact]
    public async Task ExactFamilyRequiresTrustedExplicitHashBindings()
    {
        var left = await BinaryAsync(ElfFixture.Create());
        var data = ElfFixture.Create(); data[320] = 1; var right = await BinaryAsync(data);
        var unverified = new Provenance("reported", null, DateTimeOffset.UtcNow, null, null, null, VerificationStatus.Unverified);
        var engine = new FamilyRelationshipEngine();
        FamilyBinding[] bindings = [new(left.Fingerprint.Sha256!, "family", unverified), new(right.Fingerprint.Sha256!, "family", unverified)];
        Assert.NotEqual(RelationshipStrength.ExactFamily, engine.Compare(left, right, bindings).Strength);
        var trusted = bindings.Select(b => b with { Provenance = b.Provenance with { Status = VerificationStatus.Confirmed } }).ToArray();
        Assert.Equal(RelationshipStrength.ExactFamily, engine.Compare(left, right, trusted).Strength);
        trusted[1] = trusted[1] with { GameFamilyId = "another-family" };
        Assert.Equal(RelationshipStrength.Unknown, engine.Compare(left, right, trusted).Strength);
    }

    [Fact]
    public async Task MatchingCodeInDifferentMetadataBuildsSupportsRelationshipWithoutOffsetReuse()
    {
        var left = await BinaryAsync(ElfFixture.Create());
        var other = ElfFixture.Create(); other[320] = 42;
        var right = await BinaryAsync(other);
        Assert.NotEqual(left.Fingerprint.Sha256, right.Fingerprint.Sha256);
        Assert.Equal(RelationshipStrength.Strong, new FamilyRelationshipEngine().Compare(left, right).Strength);
        Assert.Equal(RelationshipStrength.Unknown, new FamilyRelationshipEngine().Compare(left with { CodeInventoryComplete = false }, right).Strength);
    }

    [Fact]
    public async Task ResearchContinuesOnOpaqueBinaryAndRetainsLogDiagnosis()
    {
        var binary = await BinaryAsync([1, 2, 3]);
        var log = await LogAsync("Shader fatal unsupported family=MIMG");
        var result = await ServiceFactory.CreateResearchService().ResearchAsync(binary.Fingerprint.ExecutablePath!, null, log.SourcePath);
        Assert.Equal(BinaryInspectionStatus.Unsupported, result.Binary!.Status);
        Assert.Equal("KYTY EMULATOR / SHADER", result.Diagnosis.Candidates[0].Layer);
        Assert.Null(result.Evidence.Game.GameFamily);
    }
}
