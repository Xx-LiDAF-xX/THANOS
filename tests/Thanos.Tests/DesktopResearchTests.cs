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
    private static async Task ExecuteAsync(System.Windows.Input.ICommand command)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => { if (command.CanExecute(null)) { completed.TrySetResult(); } };
        command.CanExecuteChanged += handler;
        command.Execute(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        command.CanExecuteChanged -= handler;
    }

    [Fact]
    public async Task PromptAndResearchExportsAreInvalidatedWhenInputsOrModeChange()
    {
        var service = new ControlledResearch();
        var viewModel = new MainViewModel(service) { GamePath = "synthetic" };
        Assert.False(viewModel.GeneratePromptCommand.CanExecute(null));
        service.Completion.SetResult(new ResearchReport { Evidence = new AnalysisReport(1, new GameFingerprint(), null, null, []), Diagnosis = new DiagnosisEngine().Diagnose(null) });
        await ExecuteAsync(viewModel.AnalyzeCommand);
        await ExecuteAsync(viewModel.GeneratePromptCommand);
        Assert.True(viewModel.HasPrompt);
        Assert.Contains("UNKNOWN", viewModel.PromptText);
        viewModel.Mode = PromptMode.PortPatch;
        Assert.False(viewModel.HasPrompt);
        Assert.True(viewModel.HasReport);
        await ExecuteAsync(viewModel.GeneratePromptCommand);
        Assert.Equal(PromptMode.PortPatch, viewModel.CurrentPrompt!.Mode);
        viewModel.LogPath = "changed.log";
        Assert.False(viewModel.HasPrompt);
        Assert.False(viewModel.HasReport);
        Assert.Null(viewModel.CurrentResearch);
        Assert.False(viewModel.GeneratePromptCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResearchFinishedForChangedInputsCannotBeExported()
    {
        var service = new ControlledResearch();
        var viewModel = new MainViewModel(service) { GamePath = "original" };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.AnalyzeCommand.CanExecuteChanged += (_, _) => { if (viewModel.AnalyzeCommand.CanExecute(null)) { completed.TrySetResult(); } };
        viewModel.AnalyzeCommand.Execute(null);
        viewModel.GamePath = "changed";
        service.Completion.SetResult(new ResearchReport { Evidence = new AnalysisReport(1, new GameFingerprint(), null, null, []), Diagnosis = new DiagnosisEngine().Diagnose(null) });
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.HasReport);
        Assert.False(viewModel.HasPrompt);
        Assert.Contains("Inputs changed", viewModel.Status);
    }

}
