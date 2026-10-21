namespace Thanos.Core;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Stream OpenRead(string path);
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(OpenRead(path));
    IEnumerable<string> EnumerateFiles(string directory);
    bool IsSymbolicLink(string path);
}

public interface IPathService
{
    string GetFullPath(string path);
    string Combine(string directory, string name);
    string GetFileName(string path);
}

public interface IEnvironmentService
{
    string OperatingSystem { get; }
    string Architecture { get; }
    string? GetVariable(string name);
}

public interface IPlatformService { string ApplicationDataDirectory { get; } }
public interface IExecutableDiscovery { string? Find(string name); }
public interface IClipboardService { Task SetTextAsync(string text); }
public interface IArchiveService { Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken = default); }
public sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory, TimeSpan Timeout, IReadOnlyDictionary<string, string> Environment);
public sealed record ProcessResult(int? ExitCode, string StandardOutput, string StandardError, bool TimedOut);
public sealed record ProcessOutputChunk(string Text, bool IsError, DateTimeOffset Timestamp);
public interface IProcessRunner { Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default); }
public interface IStreamingProcessRunner : IProcessRunner
{
    Task<ProcessResult> RunStreamingAsync(
        ProcessRequest request,
        Action<ProcessOutputChunk>? onOutput = null,
        string? logFilePath = null,
        Action<string>? onLogLine = null,
        CancellationToken cancellationToken = default);
}

public interface IGameFingerprinter { Task<GameFingerprint> FingerprintAsync(string input, CancellationToken cancellationToken = default); }
public interface IKytyCapabilityDetector { Task<KytyFingerprint> DetectAsync(string executable, CancellationToken cancellationToken = default); }
public interface ILogParser { Task<ParsedLog> ParseAsync(string path, CancellationToken cancellationToken = default); }
public interface IAnalysisService { Task<AnalysisReport> AnalyzeAsync(string game, string? kyty, string? log, CancellationToken cancellationToken = default); }
public interface IKnowledgeStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAnalysisAsync(string id, AnalysisReport report, CancellationToken cancellationToken = default);
    Task<AnalysisReport?> GetAnalysisAsync(string id, CancellationToken cancellationToken = default);
}

// Binary inspection is implemented in Phase 2; remaining extension points follow the roadmap.
public interface IBinaryAnalyzer { Task<GameFingerprint> AnalyzeAsync(string path, CancellationToken cancellationToken = default); }
public interface ICompatibilitySource { Task<GameFamily[]> FindFamiliesAsync(GameFingerprint game, CancellationToken cancellationToken = default); }
public interface IPatchProvider { Task<PatchProposal[]> FindPatchesAsync(GameFingerprint game, CancellationToken cancellationToken = default); }
public interface IPatchImportService { Task<PatchImportResult> ImportAsync(string path, CancellationToken cancellationToken = default); }
public interface IPatchValidationService { Task<PatchValidationResult> ValidateAsync(PatchProposal patch, string gameInput, ResearchReport? research = null, CancellationToken cancellationToken = default); }
public interface IPatchTransactionService
{
    Task<PatchTransaction> ApplyAsync(PatchProposal patch, string gameInput, string transactionDirectory, string approvalPatchId, ResearchReport? research = null, CancellationToken cancellationToken = default);
    Task<PatchTransaction> InspectAsync(string transactionDirectory, CancellationToken cancellationToken = default);
    Task<PatchTransaction> RollbackAsync(string transactionDirectory, string approvalTransactionId, string? confirmedOriginalRoot = null, CancellationToken cancellationToken = default);
}
public interface IAIPromptTemplate { PromptMode Mode { get; } string Render(AnalysisReport report); }
public interface IAIPromptGenerator
{
    AIPrompt Generate(AnalysisReport report, PromptMode mode);
    AIPrompt Generate(ResearchReport report, PromptMode mode);
}
public interface IKytyAdapter
{
    string Name { get; }
    bool Supports(KytyFingerprint kyty);
    ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy);
    ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy, ScenarioDefinition scenario) => CreateRequest(kyty, workingCopy);
}

public interface IKytyAdapterRegistry
{
    void Register(IKytyAdapter adapter);
    IKytyAdapter Select(KytyFingerprint kyty);
    IReadOnlyList<IKytyAdapter> Adapters { get; }
}

public interface IKytyRunner { Task<ProcessResult> RunAsync(KytyFingerprint kyty, string workingCopy, CancellationToken cancellationToken = default); }

public interface IVerificationEngine
{
    VerificationResult Evaluate(
        ScenarioDefinition scenario,
        GameFingerprint game,
        KytyFingerprint kyty,
        string? patchId,
        string? baselineLogText,
        string? candidateLogText,
        ProcessResult? candidateExecution = null);
}

public interface IVerificationService
{
    Task<VerificationResult> VerifyAsync(
        ScenarioDefinition scenario,
        string gamePath,
        string? kytyPath = null,
        string? patchTransactionDirectory = null,
        string? baselineLogPath = null,
        string? candidateLogPath = null,
        CancellationToken cancellationToken = default);
}
