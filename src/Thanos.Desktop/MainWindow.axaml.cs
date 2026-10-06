using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Thanos.Core;
using Thanos.Desktop.ViewModels;

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
        if (await PickFile("Select game file") is { } path) { ViewModel.GamePath = path; }
    }
    private async void ChooseKyty(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select Kyty executable") is { } path) { ViewModel.KytyPath = path; }
    }
    private async void ChooseLog(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Select Kyty log") is { } path) { ViewModel.LogPath = path; }
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
    private async void CopyReport(object? sender, RoutedEventArgs e)
    {
        await new AvaloniaClipboardService(this).SetTextAsync(ViewModel.Report);
    }
}

internal sealed class AvaloniaClipboardService(TopLevel window) : IClipboardService
{
    public Task SetTextAsync(string text) => window.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
}
