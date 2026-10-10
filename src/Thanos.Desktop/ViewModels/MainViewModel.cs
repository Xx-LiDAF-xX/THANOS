using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string gamePath = "", kytyPath = "", logPath = "", knowledgePath = "", relatedPath = "", report = "", promptText = "", readiness = "";
    private string status = "Select game input to collect evidence. Capabilities and compatibility may be UNKNOWN.";
    private int revision;
    private PromptMode mode;
    private ResearchReport? research;
    private AIPrompt? prompt;
    private readonly AsyncCommand generate;
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
    public string Status { get => status; set => Set(ref status, value); }
    public ICommand AnalyzeCommand { get; }
    public ICommand GeneratePromptCommand => generate;
    public ResearchReport? CurrentResearch => research;
    public AIPrompt? CurrentPrompt => prompt;

    public MainViewModel(IResearchService analysis)
    {
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
    }
    private void ClearPrompt()
    {
        prompt = null;
        PromptText = "";
        Readiness = "";
        Changed(nameof(HasPrompt));
    }
    private void Input(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value) { return; }
        Set(ref field, value, property);
        revision++;
        research = null;
        Report = "";
        ClearPrompt();
        generate.Refresh();
    }
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    private void Set(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value) { return; }
        field = value;
        Changed(property);
    }
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
