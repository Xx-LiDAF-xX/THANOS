using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Thanos.Core;
using Thanos.Infrastructure;

namespace Thanos.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string gamePath = "";
    private string kytyPath = "";
    private string logPath = "";
    private string knowledgePath = "";
    private string relatedPath = "";
    private string report = "";
    private string status = "Select game input to collect evidence. Capabilities and compatibility may be UNKNOWN.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string GamePath { get => gamePath; set => Set(ref gamePath, value); }
    public string KytyPath { get => kytyPath; set => Set(ref kytyPath, value); }
    public string LogPath { get => logPath; set => Set(ref logPath, value); }
    public string KnowledgePath { get => knowledgePath; set => Set(ref knowledgePath, value); }
    public string RelatedPath { get => relatedPath; set => Set(ref relatedPath, value); }
    public string Report { get => report; private set { Set(ref report, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasReport))); } }
    public bool HasReport => Report.Length > 0;
    public string Status { get => status; set => Set(ref status, value); }
    public ICommand AnalyzeCommand { get; }

    public MainViewModel(IResearchService analysis)
    {
        AnalyzeCommand = new AsyncCommand(async () =>
        {
            Report = "";
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
                Report = JsonData.Serialize(result);
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
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private void Set(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value) { return; }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

internal sealed class AsyncCommand(Func<Task> action) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter)
    {
        if (running) { return; }
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
