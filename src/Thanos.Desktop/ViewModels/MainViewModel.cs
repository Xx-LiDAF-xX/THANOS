using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string gamePath = "", kytyPath = "", logPath = "", knowledgePath = "", relatedPath = "", report = "", promptText = "", readiness = "";
    private string patchPath = "", transactionPath = "", patchOutput = "";
    private string verificationOutput = "", verificationStatusText = "";
    private string status = "Select game input to collect evidence. Capabilities and compatibility may be UNKNOWN.";
    private int revision;
    private PromptMode mode;
    private ScenarioType selectedScenario = ScenarioType.Boot;
    private ResearchReport? research;
    private AIPrompt? prompt;
    private PatchProposal? normalizedPatch;
    private PatchValidationResult? patchValidation;
    private PatchTransaction? transaction;
    private VerificationResult? verificationResult;
    private readonly AsyncCommand generate;
    private readonly AsyncCommand importPatch;
    private readonly AsyncCommand validatePatch;
    private readonly AsyncCommand inspectTransaction;
    private readonly AsyncCommand verify;
    private readonly IPatchImportService patchImporter;
    private readonly IPatchValidationService patchValidator;
    private readonly IPatchTransactionService patchTransactions;
    private readonly IVerificationService verificationService;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string GamePath { get => gamePath; set => Input(ref gamePath, value); }
    public string KytyPath { get => kytyPath; set => Input(ref kytyPath, value); }
    public string LogPath { get => logPath; set => Input(ref logPath, value); }
    public string KnowledgePath { get => knowledgePath; set => Input(ref knowledgePath, value); }
    public string RelatedPath { get => relatedPath; set => Input(ref relatedPath, value); }
    public string Report { get => report; private set { Set(ref report, value); Changed(nameof(HasReport)); } }
    public bool HasReport => research is not null;
    public string PromptText { get => promptText; private set => Set(ref promptText, value); }
    public string Readiness { get => readiness; private set => Set(ref readiness, value); }
    public bool HasPrompt => prompt is not null;
    public PromptMode[] Modes { get; } = Enum.GetValues<PromptMode>();
    public PromptMode Mode { get => mode; set { if (mode == value) { return; } mode = value; Changed(); ClearPrompt(); } }
    public ScenarioType[] Scenarios { get; } = Enum.GetValues<ScenarioType>();
    public ScenarioType SelectedScenario { get => selectedScenario; set { if (Set(ref selectedScenario, value)) { Changed(); } } }
    public string Status { get => status; set => Set(ref status, value); }
    public string PatchPath { get => patchPath; set { if (Set(ref patchPath, value)) { ClearPatch(); importPatch.Refresh(); } } }
    public string TransactionPath { get => transactionPath; set { if (Set(ref transactionPath, value)) { transaction = null; Changed(nameof(HasTransaction)); inspectTransaction.Refresh(); } } }
    public string PatchOutput { get => patchOutput; private set => Set(ref patchOutput, value); }
    public bool HasPatch => normalizedPatch is not null;
    public bool HasValidPatch => patchValidation?.Status == PatchValidationStatus.Valid;
    public bool HasTransaction => transaction is not null;
    public string VerificationOutput { get => verificationOutput; private set => Set(ref verificationOutput, value); }
    public string VerificationStatusText { get => verificationStatusText; private set => Set(ref verificationStatusText, value); }
    public bool HasVerification => verificationResult is not null;
    public VerificationResult? CurrentVerification => verificationResult;
    public ICommand AnalyzeCommand { get; }
    public ICommand GeneratePromptCommand => generate;
    public ICommand ImportPatchCommand => importPatch;
    public ICommand ValidatePatchCommand => validatePatch;
    public ICommand InspectTransactionCommand => inspectTransaction;
    public ICommand VerifyCommand => verify;
    public ResearchReport? CurrentResearch => research;
    public AIPrompt? CurrentPrompt => prompt;
    public PatchProposal? CurrentPatch => normalizedPatch;
    public PatchTransaction? CurrentTransaction => transaction;

    public MainViewModel(IResearchService analysis, IPatchImportService? patchImporter = null, IPatchValidationService? patchValidator = null, IPatchTransactionService? patchTransactions = null, IVerificationService? verificationService = null)
    {
        this.patchImporter = patchImporter ?? ServiceFactory.CreatePatchImporter();
        this.patchValidator = patchValidator ?? ServiceFactory.CreatePatchValidator();
        this.patchTransactions = patchTransactions ?? ServiceFactory.CreatePatchTransactions();
        this.verificationService = verificationService ?? ServiceFactory.CreateVerificationService();
        importPatch = new AsyncCommand(async () =>
        {
            try
            {
                ClearPatch();
                var imported = await this.patchImporter.ImportAsync(PatchPath);
                normalizedPatch = imported.Patch;
                PatchOutput = JsonData.Serialize(imported);
                Changed(nameof(HasPatch)); validatePatch?.Refresh();
                Status = imported.Status == PatchImportStatus.Normalized ? "Patch normalized for validation. No imported code was executed." : $"Patch is {imported.Status}; inspect the findings. Nothing was executed.";
            }
            catch (Exception ex) when (PatchFailure(ex)) { Status = $"Patch import failed: {ex.Message}"; }
        }, () => !string.IsNullOrWhiteSpace(PatchPath));
        validatePatch = new AsyncCommand(async () =>
        {
            var patch = normalizedPatch;
            if (patch is null) { return; }
            try
            {
                patchValidation = await this.patchValidator.ValidateAsync(patch, GamePath, research);
                PatchOutput = JsonData.Serialize(patchValidation);
                Changed(nameof(HasValidPatch));
                Status = patchValidation.Status == PatchValidationStatus.Valid ? "Patch bytes validated. Compatibility remains unverified; explicit approval is required to create a working copy." : "PATCH REJECTED. Review failed critical checks; no nearby target was tried.";
            }
            catch (Exception ex) when (PatchFailure(ex)) { ClearValidation(); Status = $"Patch validation failed: {ex.Message}"; }
        }, () => normalizedPatch is not null && !string.IsNullOrWhiteSpace(GamePath));
        inspectTransaction = new AsyncCommand(async () =>
        {
            try
            {
                transaction = await this.patchTransactions.InspectAsync(TransactionPath);
                PatchOutput = JsonData.Serialize(transaction);
                Changed(nameof(HasTransaction));
                Status = $"Transaction {transaction.TransactionId} inspected: {transaction.Status}.";
            }
            catch (Exception ex) when (PatchFailure(ex)) { transaction = null; Changed(nameof(HasTransaction)); Status = $"Transaction inspection failed: {ex.Message}"; }
        }, () => !string.IsNullOrWhiteSpace(TransactionPath));
        generate = new AsyncCommand(async () =>
        {
            var source = research;
            var selectedMode = Mode;
            ClearPrompt();
            if (source is null) { return; }
            Status = "Generating evidence request…";
            var result = await Task.Run(() => new PromptGenerator().Generate(source, selectedMode));
            if (!ReferenceEquals(source, research) || selectedMode != Mode) { Status = "Inputs or mode changed; generate a new prompt."; return; }
            prompt = result;
            PromptText = result.Text;
            Readiness = $"Evidence readiness: {result.Readiness:P0} · {result.MissingEvidence.Length} missing items. This is not compatibility or patch approval.";
            Changed(nameof(HasPrompt));
            Status = "Prompt ready for review. No AI service was contacted.";
        }, () => research is not null);
        AnalyzeCommand = new AsyncCommand(async () =>
        {
            research = null;
            Report = "";
            ClearPrompt();
            generate.Refresh();
            var inputRevision = revision;
            Status = "Collecting accessible evidence…";
            try
            {
                var game = GamePath;
                var kyty = Optional(KytyPath);
                var log = Optional(LogPath);
                var knowledgeFile = Optional(KnowledgePath);
                var related = Optional(RelatedPath);
                var result = await Task.Run(async () =>
                {
                    var knowledge = knowledgeFile is null ? null : await KnowledgeJson.ReadAsync(knowledgeFile);
                    return await analysis.ResearchAsync(game, kyty, log, knowledge, related);
                });
                if (inputRevision != revision) { Status = "Inputs changed during analysis; analyze the current inputs."; return; }
                research = result;
                Report = JsonData.Serialize(result);
                generate.Refresh();
                var candidate = result.Diagnosis.Candidates.FirstOrDefault();
                Status = $"Research collected · Root-cause candidate: {candidate?.Layer ?? "UNKNOWN"} · Confidence: {candidate?.Confidence:P0}. Hypotheses require verification.";
                new AppLogger(new LocalPlatformService()).Write("ANALYZER", "Desktop analysis completed");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                Status = $"Analysis failed: {ex.Message}";
            }
        });

        verify = new AsyncCommand(async () =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(GamePath))
                {
                    VerificationStatusText = "Error: Select a game folder or executable.";
                    return;
                }
                VerificationStatusText = $"Running verification scenario {selectedScenario}...";
                var scenario = ScenarioDefinition.CreateBuiltIn(selectedScenario);
                var candidateLog = !string.IsNullOrWhiteSpace(LogPath) && File.Exists(LogPath) ? LogPath : null;
                var kyty = !string.IsNullOrWhiteSpace(KytyPath) && File.Exists(KytyPath) ? KytyPath : null;
                var txDir = !string.IsNullOrWhiteSpace(TransactionPath) && Directory.Exists(TransactionPath) ? TransactionPath : null;

                var res = await this.verificationService.VerifyAsync(scenario, GamePath, kyty, txDir, null, candidateLog);
                VerificationOutput = JsonData.Serialize(res);
                VerificationStatusText = $"Outcome: {res.Outcome} · Level: {res.Level} · Scenario: {res.Scenario}";
                verificationResult = res;
                Changed(nameof(HasVerification));
                Changed(nameof(CurrentVerification));
            }
            catch (Exception ex)
            {
                VerificationStatusText = $"Verification failed: {ex.Message}";
                VerificationOutput = ex.ToString();
            }
        });
    }

    public async Task ApplyApprovedAsync()
    {
        var patch = normalizedPatch;
        if (patch is null || patchValidation?.Status != PatchValidationStatus.Valid) { Status = "Validate the current patch and target before approval."; return; }
        try
        {
            Status = "Creating backup and separate working copy…";
            transaction = await patchTransactions.ApplyAsync(patch, GamePath, TransactionPath, patch.PatchId, research);
            PatchOutput = JsonData.Serialize(transaction);
            Changed(nameof(HasTransaction));
            Status = $"Working-copy transaction created: {transaction.TransactionId}. The original was not modified.";
        }
        catch (Exception ex) when (PatchFailure(ex)) { Status = $"Patch transaction failed: {ex.Message}"; }
    }

    public async Task RollbackApprovedAsync(bool restoreOriginal)
    {
        var current = transaction;
        if (current is null) { Status = "Inspect a transaction before rollback."; return; }
        try
        {
            transaction = await patchTransactions.RollbackAsync(TransactionPath, current.TransactionId, restoreOriginal ? current.OriginalRoot : null);
            PatchOutput = JsonData.Serialize(transaction);
            Status = restoreOriginal ? "Rollback removed the working copy and restored the recorded backup. Displaced bytes, if any, were preserved." : "Rollback removed the working copy. The original remains unchanged.";
        }
        catch (Exception ex) when (PatchFailure(ex)) { Status = $"Rollback failed: {ex.Message}"; }
    }
    private void ClearPrompt()
    {
        prompt = null;
        PromptText = "";
        Readiness = "";
        Changed(nameof(HasPrompt));
    }
    private void ClearPatch()
    {
        normalizedPatch = null; PatchOutput = ""; ClearValidation();
        Changed(nameof(HasPatch)); validatePatch?.Refresh();
    }
    private void ClearValidation()
    {
        patchValidation = null; Changed(nameof(HasValidPatch));
    }
    private void Input(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value) { return; }
        Set(ref field, value, property);
        revision++;
        research = null;
        Report = "";
        ClearPrompt();
        ClearValidation();
        generate.Refresh();
        validatePatch.Refresh();
    }
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) { return false; }
        field = value;
        Changed(property);
        return true;
    }
    private static bool PatchFailure(Exception ex) => ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException;
}

internal sealed class AsyncCommand(Func<Task> action, Func<bool>? allowed = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && (allowed?.Invoke() ?? true);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) { return; }
        running = true;
        Refresh();
        try { await action(); }
        finally { running = false; Refresh(); }
    }
}
