using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Thanos.Core;
using Thanos.Desktop.ViewModels;
using Thanos.Infrastructure;

namespace Thanos.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private async Task<string?> PickFile(string title)
    {
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return selected.Count == 1 ? selected[0].TryGetLocalPath() : null;
    }
    private async void ChooseGameFolder(object? sender, RoutedEventArgs e)
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select game folder", AllowMultiple = false });
        if (selected.Count == 1 && selected[0].TryGetLocalPath() is { } path) { ViewModel.GamePath = path; }
    }
    private async void ChooseGameFile(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select executable, game archive or package") is { } path) { ViewModel.GamePath = path; }
    }
    private async void ChooseKyty(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select Kyty executable") is { } path) { ViewModel.KytyPath = path; }
    }
    private async void ChooseLog(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select Kyty log or log archive") is { } path) { ViewModel.LogPath = path; }
    }
    private async void ChooseKnowledge(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select knowledge JSON") is { } path) { ViewModel.KnowledgePath = path; }
    }
    private async void ChooseRelated(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select related executable") is { } path) { ViewModel.RelatedPath = path; }
    }
    private async void ChoosePatch(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select AI patch output for inspection") is { } path) { ViewModel.PatchPath = path; }
    }
    private async void ChooseTransactionParent(object? sender, RoutedEventArgs e)
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select parent for a new transaction, or select an existing transaction", AllowMultiple = false });
        if (selected.Count != 1 || selected[0].TryGetLocalPath() is not { } path) { return; }
        ViewModel.TransactionPath = File.Exists(Path.Combine(path, "transaction.json")) ? path : Path.Combine(path, "thanos-transaction-" + (ViewModel.CurrentPatch?.PatchId ?? "new"));
    }
    private async void ExportReport(object? sender, RoutedEventArgs e)
    {
        try
        {
            var report = ViewModel.Report;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export evidence", SuggestedFileName = "analysis.json" });
            if (file is null) { return; }
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(report);
            ViewModel.Status = "Evidence exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ViewModel.Status = $"Export failed: {ex.Message}";
        }
    }
    private async void CopyPrompt(object? sender, RoutedEventArgs e)
    {
        await new AvaloniaClipboardService(this).SetTextAsync(ViewModel.PromptText);
    }
    private async void ExportPrompt(object? sender, RoutedEventArgs e) => await ExportPromptAsync(false);
    private async void ExportBundle(object? sender, RoutedEventArgs e) => await ExportPromptAsync(true);
    private async Task ExportPromptAsync(bool bundle)
    {
        var prompt = ViewModel.CurrentPrompt;
        var research = ViewModel.CurrentResearch;
        if (prompt is null || research is null) { return; }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = bundle ? "Export evidence package to a new file" : "Export prompt to a new file",
                SuggestedFileName = bundle ? "evidence.zip" : "prompt.md"
            });
            if (file is null) { return; }
            if (!ReferenceEquals(prompt, ViewModel.CurrentPrompt) || !ReferenceEquals(research, ViewModel.CurrentResearch))
            {
                ViewModel.Status = "Evidence changed during selection; generate and export again.";
                return;
            }
            var path = file.TryGetLocalPath() ?? throw new NotSupportedException("Select a local export destination.");
            await Task.Run(async () =>
            {
                if (bundle) { await PromptExporter.WritePackageAsync(research, prompt, path); }
                else { await PromptExporter.WriteTextAsync(prompt, path); }
            });
            ViewModel.Status = "Export completed. Review local paths and logs before sharing.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            ViewModel.Status = $"Export failed: {ex.Message}";
        }
    }
    private async void CopyReport(object? sender, RoutedEventArgs e)
    {
        await new AvaloniaClipboardService(this).SetTextAsync(ViewModel.Report);
    }
    private async void ApplyPatch(object? sender, RoutedEventArgs e)
    {
        var patch = ViewModel.CurrentPatch;
        if (patch is null || string.IsNullOrWhiteSpace(ViewModel.TransactionPath)) { ViewModel.Status = "Select a new transaction directory before applying."; return; }
        if (await ConfirmAsync("Create validated working copy?", $"Approve patch ID {patch.PatchId}?\n\nTHANOS will copy the game, preserve a target backup, and modify only the working copy at {ViewModel.TransactionPath}. Compatibility remains unverified."))
        {
            await ViewModel.ApplyApprovedAsync();
        }
    }
    private async void RollbackPatch(object? sender, RoutedEventArgs e)
    {
        var transaction = ViewModel.CurrentTransaction;
        if (transaction is not null && await ConfirmAsync("Rollback working copy?", $"Approve transaction ID {transaction.TransactionId}?\n\nThe transaction working directory will be deleted. Its backup and metadata will remain."))
        {
            await ViewModel.RollbackApprovedAsync(false);
        }
    }
    private async void RestoreOriginal(object? sender, RoutedEventArgs e)
    {
        var transaction = ViewModel.CurrentTransaction;
        if (transaction is not null && await ConfirmAsync("Restore exact original target?", $"Approve transaction ID {transaction.TransactionId} and original root:\n{transaction.OriginalRoot}\n\nThe recorded backup will replace the target. Displaced bytes will first be copied into this transaction's recovery folder."))
        {
            await ViewModel.RollbackApprovedAsync(true);
        }
    }
    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var approve = new Button { Content = "Approve", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        approve.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, approve } } }
        };
        return await dialog.ShowDialog<bool>(this);
    }
}

internal sealed class AvaloniaClipboardService(TopLevel window) : IClipboardService
{
    public Task SetTextAsync(string text) => window.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
}
