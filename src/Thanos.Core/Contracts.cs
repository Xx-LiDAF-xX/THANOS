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
public interface IProcessRunner { Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default); }

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
public interface IAIPromptTemplate { PromptMode Mode { get; } string Render(AnalysisReport report); }
public interface IAIPromptGenerator
{
    AIPrompt Generate(AnalysisReport report, PromptMode mode);
    AIPrompt Generate(ResearchReport report, PromptMode mode);
}
public interface IKytyAdapter { bool Supports(KytyFingerprint kyty); ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy); }
public interface IKytyRunner { Task<ProcessResult> RunAsync(KytyFingerprint kyty, string workingCopy, CancellationToken cancellationToken = default); }
