using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class VerificationService(
    IGameFingerprinter gameFingerprinter,
    IKytyCapabilityDetector kytyDetector,
    IKytyAdapterRegistry adapterRegistry,
    IStreamingProcessRunner processRunner,
    IVerificationEngine verificationEngine,
    IPatchTransactionService patchTransactionService) : IVerificationService
{
    public async Task<VerificationResult> VerifyAsync(
        ScenarioDefinition scenario,
        string gamePath,
        string? kytyPath = null,
        string? patchTransactionDirectory = null,
        string? baselineLogPath = null,
        string? candidateLogPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var game = await gameFingerprinter.FingerprintAsync(gamePath, cancellationToken);

        KytyFingerprint kyty;
        if (!string.IsNullOrWhiteSpace(kytyPath) && File.Exists(kytyPath))
        {
            kyty = await kytyDetector.DetectAsync(kytyPath, cancellationToken);
        }
        else
        {
            kyty = new KytyFingerprint
            {
                ExecutablePath = kytyPath ?? "UNKNOWN",
                Sha256 = new string('0', 64),
                FileSize = 0,
                CapabilityStatus = "Unknown"
            };
        }

        string? baselineLogText = null;
        if (!string.IsNullOrWhiteSpace(baselineLogPath) && File.Exists(baselineLogPath))
        {
            baselineLogText = await File.ReadAllTextAsync(baselineLogPath, cancellationToken);
        }

        string? patchId = null;
        string workingGameTarget = gamePath;

        if (!string.IsNullOrWhiteSpace(patchTransactionDirectory) && Directory.Exists(patchTransactionDirectory))
        {
            var tx = await patchTransactionService.InspectAsync(patchTransactionDirectory, cancellationToken);
            patchId = tx.PatchId;
            var workingDir = Path.Combine(patchTransactionDirectory, "working");
            if (Directory.Exists(workingDir))
            {
                workingGameTarget = workingDir;
            }
        }

        // Offline mode: candidate log supplied
        if (!string.IsNullOrWhiteSpace(candidateLogPath) && File.Exists(candidateLogPath))
        {
            var candidateLogText = await File.ReadAllTextAsync(candidateLogPath, cancellationToken);
            return verificationEngine.Evaluate(scenario, game, kyty, patchId, baselineLogText, candidateLogText);
        }

        // Live execution mode: requires valid kyty executable
        if (string.IsNullOrWhiteSpace(kytyPath) || !File.Exists(kytyPath))
        {
            throw new FileNotFoundException("A valid Kyty emulator executable is required for live execution.", kytyPath);
        }

        var adapter = adapterRegistry.Select(kyty);
        var request = adapter.CreateRequest(kyty, workingGameTarget, scenario);

        var execResult = await processRunner.RunStreamingAsync(
            request,
            onOutput: null,
            logFilePath: null,
            onLogLine: null,
            cancellationToken: cancellationToken);

        var executionOutput = string.IsNullOrWhiteSpace(execResult.StandardError)
            ? execResult.StandardOutput
            : execResult.StandardOutput + "\n" + execResult.StandardError;

        return verificationEngine.Evaluate(scenario, game, kyty, patchId, baselineLogText, executionOutput, execResult);
    }
}
