# Desktop

Avalonia cross-platform XAML/MVVM shell. Run `dotnet run --project src/Thanos.Desktop -c Release` from the repository root. Select game input and optional Kyty/log/knowledge/related-build paths, then Analyze; export/copy Phase 2 research JSON. MainWindow handles file dialogs/clipboard; App composes shared research services. View-model tests cover concurrent-run prevention and clearing stale evidence on failure. XAML compiles on the platform matrix. Native rendering/interactive acceptance remain manual, and tests do not establish game compatibility.
