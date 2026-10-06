using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Thanos.Desktop.ViewModels;
using Thanos.Infrastructure;

namespace Thanos.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(ServiceFactory.CreateResearchService()) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
