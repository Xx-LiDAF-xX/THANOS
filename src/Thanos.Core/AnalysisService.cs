namespace Thanos.Core;

public sealed class AnalysisService(IGameFingerprinter games, IKytyCapabilityDetector kytyDetector, ILogParser logs) : IAnalysisService
{
    public async Task<AnalysisReport> AnalyzeAsync(string game, string? kyty, string? log, CancellationToken cancellationToken = default)
    {
        var fingerprint = await games.FingerprintAsync(game, cancellationToken);
        var emulator = kyty is null ? null : await kytyDetector.DetectAsync(kyty, cancellationToken);
        var parsedLog = log is null ? null : await logs.ParseAsync(log, cancellationToken);
        return new AnalysisReport(1, fingerprint, emulator, parsedLog,
            ["Phase 1 collects evidence only. Root-cause analysis, family matching, prompts, patches and emulator execution are not implemented."]);
    }
}
