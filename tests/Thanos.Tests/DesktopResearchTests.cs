using Thanos.Core;
using Thanos.Desktop.ViewModels;

namespace Thanos.Tests;

public sealed class DesktopResearchTests
{
    private sealed class ControlledResearch : IResearchService
    {
        public TaskCompletionSource<ResearchReport> Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ResearchReport> ResearchAsync(string game, string? kyty, string? log, KnowledgeBundle? knowledge = null, string? relatedBuild = null, CancellationToken cancellationToken = default) => Completion.Task;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopPreventsConcurrentResearchAndClearsStaleEvidenceAfterFailure(bool invalidData)
    {
        var service = new ControlledResearch();
        var viewModel = new MainViewModel(service) { GamePath = "synthetic-file" };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.AnalyzeCommand.CanExecuteChanged += (_, _) => { if (viewModel.AnalyzeCommand.CanExecute(null)) { finished.TrySetResult(); } };
        viewModel.AnalyzeCommand.Execute(null);
        Assert.False(viewModel.AnalyzeCommand.CanExecute(null));
        Assert.False(viewModel.HasReport);
        service.Completion.SetResult(new ResearchReport { Evidence = new AnalysisReport(1, new GameFingerprint(), null, null, []), Diagnosis = new DiagnosisEngine().Diagnose(null) });
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.HasReport);
        Assert.Contains("UNKNOWN", viewModel.Status);
        service.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.AnalyzeCommand.Execute(null);
        service.Completion.SetException(invalidData ? new InvalidDataException("synthetic input error") : new IOException("synthetic input error"));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.HasReport);
        Assert.Equal("", viewModel.Report);
        Assert.Contains("synthetic input error", viewModel.Status);
    }
}
