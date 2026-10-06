# Desktop

Avalonia cross-platform XAML/MVVM shell. Run `dotnet run --project src/Thanos.Desktop -c Release` from the repository root. Select game input, optional Kyty file/log and Analyze; export or copy JSON. View-model state contains no binary/log analysis logic. MainWindow integrates file dialogs/clipboard; App composes shared services. XAML is compiled on the CI platform matrix. Native rendering and interactive UI acceptance checks remain manual; no game compatibility claims arise from compilation.
