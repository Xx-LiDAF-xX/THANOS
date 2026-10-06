using System.Text.Json;
using System.Text.RegularExpressions;
using Thanos.Infrastructure;
using Thanos.Infrastructure.Storage;

namespace Thanos.Cli;

internal static class Program
{
    private const string Help = """
        THANOS 0.1.0 — Kyty compatibility evidence collector (Phase 1)

        kytyfixer fingerprint <game-directory-or-file>
        kytyfixer detect-kyty <executable>
        kytyfixer parse-log <log>
        kytyfixer analyze <game> [--kyty <file>] [--log <file>] [--database <db>]
        kytyfixer database init <db>
        kytyfixer database export <db> <analysis-id>
        kytyfixer help

        Commands emit JSON to stdout. Redirect to a file for export.
        analyze --database saves a uniquely identified report; its id is printed to stderr.
        Exit codes: 0 success (may be PARTIAL), 2 usage, 3 input/storage error, 130 canceled.
        Diagnose, generate-prompt, validate-patch, apply-patch, test, compare, benchmark
        and report are roadmap commands, not implemented in Phase 1.
        """;

    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        try
        {
            if (args.Length == 0 || args is ["help"] or ["--help"] or ["-h"])
            {
                Console.WriteLine(Help);
                return 0;
            }
            var token = cancellation.Token;
            var files = new LocalFileSystem();
            var paths = new LocalPathService();
            var fingerprints = new GameFingerprinter(files, paths);
            object result;
            switch (args)
            {
                case ["fingerprint", var input]:
                    result = await fingerprints.FingerprintAsync(input, token);
                    break;
                case ["detect-kyty", var executable]:
                    result = await new KytyCapabilityDetector(fingerprints).DetectAsync(executable, token);
                    break;
                case ["parse-log", var log]:
                    result = await new KytyLogParser(files, paths).ParseAsync(log, token);
                    break;
                case ["database", "init", var database]:
                    await new SqliteKnowledgeStore(database).InitializeAsync(token);
                    result = new { schemaVersion = 1, status = "initialized" };
                    break;
                case ["database", "export", var database, var id]:
                    result = await new SqliteKnowledgeStore(database).GetAnalysisAsync(id, token)
                        ?? throw new InvalidDataException("Analysis id not found.");
                    break;
                default:
                    if (args.Length < 2 || args[0] != "analyze")
                    {
                        Console.Error.WriteLine(Help);
                        return 2;
                    }
                    var options = new Dictionary<string, string>();
                    for (var i = 2; i < args.Length; i += 2)
                    {
                        if (i + 1 >= args.Length || args[i] is not ("--kyty" or "--log" or "--database") || !options.TryAdd(args[i], args[i + 1]))
                        {
                            Console.Error.WriteLine("Invalid, duplicate or incomplete analyze option.");
                            return 2;
                        }
                    }
                    options.TryGetValue("--kyty", out var kyty);
                    options.TryGetValue("--log", out var logPath);
                    var report = await ServiceFactory.CreateAnalysisService().AnalyzeAsync(args[1], kyty, logPath, token);
                    if (options.TryGetValue("--database", out var db))
                    {
                        var store = new SqliteKnowledgeStore(db);
                        await store.InitializeAsync(token);
                        var reportId = Guid.NewGuid().ToString("N");
                        await store.SaveAnalysisAsync(reportId, report, token);
                        Console.Error.WriteLine($"Saved analysis: {reportId}");
                    }
                    result = report;
                    break;
            }
            Console.WriteLine(JsonData.Serialize(result));
            new AppLogger(new LocalPlatformService()).Write("ANALYZER", $"Completed {args[0]}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 130;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or RegexMatchTimeoutException or Microsoft.Data.Sqlite.SqliteException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 3;
        }
    }
}
