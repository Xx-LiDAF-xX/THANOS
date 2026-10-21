using System.Text.Json;
using System.Text.RegularExpressions;
using Thanos.Core;
using Thanos.Infrastructure;
using Thanos.Infrastructure.Storage;

namespace Thanos.Cli;

public static class Program
{
    private const string Help = """
        THANOS 0.5.0 — Kyty compatibility research, patch transactions and emulator verification (Phase 5)

        kytyfixer fingerprint <game-directory-file-or-archive>
        kytyfixer detect-kyty <executable>
        kytyfixer parse-log <log-or-archive>
        kytyfixer list-input <archive>
        kytyfixer generate-prompt <game> [--kyty <file>] [--log <file>] [--related <file>] [--knowledge <json> | --database <db>] [--mode <mode>] [--output <new.md>] [--bundle <new.zip>]
        kytyfixer generate-prompt <analysis-or-research.json> [--mode <mode>] [--output <new.md>] [--bundle <new.zip>]
        kytyfixer generate-prompt --report <analysis-or-research.json> [--mode <mode>] [--output <new.md>] [--bundle <new.zip>]
        kytyfixer normalize-patch <ai-output> [--output <new-patch.json>]
        kytyfixer validate-patch <patch.json> --game <file-or-folder> [--research <research.json>]
        kytyfixer apply-patch <patch.json> --game <file-or-folder> --transaction <new-directory> --approve <patch-id> [--research <research.json>]
        kytyfixer rollback <transaction-directory> --approve <transaction-id> [--restore-original <exact-original-root>]
        kytyfixer verify <game> [--kyty <file>] [--scenario <BOOT|MENU|GAMEPLAY|CRASH REPRODUCTION|STABILITY|PERFORMANCE>] [--candidate-log <file>] [--baseline-log <file>] [--transaction <dir>] [--timeout <sec>] [--output <file>] [--database <db>]
        kytyfixer analyze <game> [--kyty <file>] [--log <file>] [--database <db>]
        kytyfixer diagnose <game> [--kyty <file>] [--log <file>] [--related <file>] [--knowledge <json> | --database <db>]
        kytyfixer binary-info <file>
        kytyfixer map-address <file> <fileOffset|virtualAddress|runtimeAddress> <0xVALUE> <destination-space> [<0xLOAD_BIAS>]
        kytyfixer compare-builds <file-a> <file-b> [--knowledge <json>]
        kytyfixer diff-binaries <original-file> <candidate-file>
        kytyfixer database init <db>
        kytyfixer database export <db> <analysis-id>
        kytyfixer database export-research <db> <research-id>
        kytyfixer knowledge import <db> <json>
        kytyfixer knowledge export <db>
        kytyfixer help

        Commands emit JSON to stdout. Redirect to a file for export.
        analyze --database saves a uniquely identified report; its id is printed to stderr.
        Exit codes: 0 success (may be PARTIAL), 2 usage, 3 input/storage error, 130 canceled.
        analyze preserves the Phase 1 JSON contract; diagnose emits a Phase 2 research report.
        ZIP is built in. RAR/7z/tar require libarchive bsdtar. ZAR requires Python (3.14,
        or zstandard installed). PKG is package evidence only; no executable decryption.
        Select an archive member explicitly: "game.zip!/Game/eboot.bin".
        Log byte count, line count and line length have no configured limits.
        Prompt modes: diagnose, findExistingFix, writePatch, portPatch, compareBuilds,
        investigateEmulatorIssue, optimizePerformance, deepReverseEngineering.
        Prompt commands emit AIPrompt JSON; --output exports text and --bundle packages
        complete research/prompt evidence with SHA-256 manifest. Existing exports are not replaced.
        Patch JSON is validated against exact hashes, ELF mappings, signatures, bytes and
        optional identity/version research. Apply writes only a separate working copy.
        Scripts/diffs/binary patches are inspect-only and never executed automatically.
        verify runs capability-backed Kyty execution or evaluates offline run logs against
        verification levels 0-7, reporting PASS/FAIL/IMPROVED/REGRESSED/INCONCLUSIVE.
        """;

    public static async Task<int> Main(string[] args)
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
                case ["list-input", var archivePath]:
                    result = new { sourcePath = Path.GetFullPath(archivePath), members = await ContainerInputs.ListAsync(archivePath, token) };
                    break;
                case ["detect-kyty", var executable]:
                    result = await new KytyCapabilityDetector(fingerprints).DetectAsync(executable, token);
                    break;
                case ["parse-log", var log]:
                    result = await new KytyLogParser(files, paths).ParseAsync(log, token);
                    break;
                case ["binary-info", var binaryPath]:
                    result = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(binaryPath, token);
                    break;
                case ["map-address", var binaryPath, var sourceSpace, var value, var targetSpace, .. var trailing] when trailing.Length <= 1:
                    if (!Enum.TryParse<AddressSpace>(sourceSpace, true, out var source) || !Enum.IsDefined(source) || !Enum.TryParse<AddressSpace>(targetSpace, true, out var target) || !Enum.IsDefined(target))
                    {
                        Console.Error.WriteLine("Unrecognized address space."); return 2;
                    }
                    var inspection = await ServiceFactory.CreateBinaryAnalyzer().InspectAsync(binaryPath, token);
                    ulong? bias = args.Length == 6 ? AddressMapper.Parse(new BinaryAddress(AddressSpace.VirtualAddress, args[5])) : null;
                    result = new AddressMapper().Map(inspection, new BinaryAddress(source, value), target, bias);
                    break;
                case ["compare-builds", var left, var right, .. var trailing] when trailing.Length == 0 || trailing is ["--knowledge", _]:
                    var comparisonKnowledge = args.Length == 5 ? await KnowledgeJson.ReadAsync(args[4], token) : new KnowledgeBundle();
                    var inspector = ServiceFactory.CreateBinaryAnalyzer();
                    result = new FamilyRelationshipEngine().Compare(await inspector.InspectAsync(left, token), await inspector.InspectAsync(right, token), comparisonKnowledge.Bindings);
                    break;
                case ["diff-binaries", var original, var candidate]:
                    if (!File.Exists(original) || !File.Exists(candidate)) { throw new InvalidDataException("Select two explicit binary files."); }
                    result = await new BinaryDiffer(files, fingerprints).CompareAsync(original, candidate, token);
                    break;
                case ["database", "init", var database]:
                    await new SqliteKnowledgeStore(database).InitializeAsync(token);
                    result = new { schemaVersion = 2, status = "initialized" };
                    break;
                case ["database", "export", var database, var id]:
                    result = await new SqliteKnowledgeStore(database).GetAnalysisAsync(id, token)
                        ?? throw new InvalidDataException("Analysis id not found.");
                    break;
                case ["database", "export-research", var database, var id]:
                    result = await new SqliteKnowledgeStore(database).GetResearchAsync(id, token) ?? throw new InvalidDataException("Research id not found.");
                    break;
                case ["knowledge", "import", var database, var jsonPath]:
                    var imported = await KnowledgeJson.ReadAsync(jsonPath, token);
                    var importStore = new SqliteKnowledgeStore(database);
                    await importStore.InitializeAsync(token);
                    await importStore.ImportKnowledgeAsync(imported, token);
                    result = new { schemaVersion = 1, status = "imported", provenancePreserved = true };
                    break;
                case ["knowledge", "export", var database]:
                    var exportStore = new SqliteKnowledgeStore(database);
                    await exportStore.InitializeAsync(token);
                    result = await exportStore.ExportKnowledgeAsync(token);
                    break;
                case ["normalize-patch", var patchInput, .. var normalizeTrailing]:
                    if (normalizeTrailing is not ([] or ["--output", _])) { Console.Error.WriteLine("Usage: normalize-patch <ai-output> [--output <new-patch.json>]"); return 2; }
                    var importedPatch = await ServiceFactory.CreatePatchImporter().ImportAsync(patchInput, token);
                    if (normalizeTrailing is ["--output", var normalizedOutput])
                    {
                        if (importedPatch.Patch is null) { throw new InvalidDataException("Input could not be normalized to actionable PatchProposal JSON."); }
                        await WriteNewJsonAsync(normalizedOutput, importedPatch.Patch, token);
                    }
                    result = importedPatch;
                    break;
                case ["validate-patch", var validationInput, .. var validationTrailing]:
                    var validationOptions = ParseOptions(validationTrailing, "--game", "--research");
                    if (validationOptions is null || !validationOptions.TryGetValue("--game", out var validationGame)) { Console.Error.WriteLine("Usage: validate-patch <patch.json> --game <file-or-folder> [--research <research.json>]"); return 2; }
                    var validationPatch = await RequirePatchAsync(validationInput, token);
                    var validationResearch = validationOptions.TryGetValue("--research", out var validationResearchPath) ? await ResearchJson.ReadAsync(validationResearchPath, token) : null;
                    result = await ServiceFactory.CreatePatchValidator().ValidateAsync(validationPatch, validationGame, validationResearch, token);
                    break;
                case ["apply-patch", var applyInput, .. var applyTrailing]:
                    var applyOptions = ParseOptions(applyTrailing, "--game", "--transaction", "--approve", "--research");
                    if (applyOptions is null || !applyOptions.TryGetValue("--game", out var applyGame) || !applyOptions.TryGetValue("--transaction", out var transactionDirectory) || !applyOptions.TryGetValue("--approve", out var patchApproval))
                    {
                        Console.Error.WriteLine("Usage: apply-patch <patch.json> --game <file-or-folder> --transaction <new-directory> --approve <patch-id> [--research <research.json>]"); return 2;
                    }
                    var applyPatch = await RequirePatchAsync(applyInput, token);
                    var applyResearch = applyOptions.TryGetValue("--research", out var applyResearchPath) ? await ResearchJson.ReadAsync(applyResearchPath, token) : null;
                    result = await ServiceFactory.CreatePatchTransactions().ApplyAsync(applyPatch, applyGame, transactionDirectory, patchApproval, applyResearch, token);
                    break;
                case ["rollback", var rollbackDirectory, .. var rollbackTrailing]:
                    var rollbackOptions = ParseOptions(rollbackTrailing, "--approve", "--restore-original");
                    if (rollbackOptions is null || !rollbackOptions.TryGetValue("--approve", out var transactionApproval)) { Console.Error.WriteLine("Usage: rollback <transaction-directory> --approve <transaction-id> [--restore-original <exact-original-root>]"); return 2; }
                    rollbackOptions.TryGetValue("--restore-original", out var confirmedOriginal);
                    result = await ServiceFactory.CreatePatchTransactions().RollbackAsync(rollbackDirectory, transactionApproval, confirmedOriginal, token);
                    break;
                case ["verify", .. var verifyTrailing]:
                    var verifyPositionalGame = verifyTrailing.Length > 0 && !verifyTrailing[0].StartsWith("--") ? verifyTrailing[0] : null;
                    var verifyOptionsArgs = verifyPositionalGame is not null ? verifyTrailing[1..] : verifyTrailing;
                    var verifyOptions = ParseOptions(verifyOptionsArgs, "--game", "--kyty", "--scenario", "--candidate-log", "--baseline-log", "--transaction", "--timeout", "--output", "--database");
                    if (verifyOptions is null)
                    {
                        Console.Error.WriteLine("Usage: verify [<game>] [--game <file-or-folder>] [--kyty <executable>] [--scenario <name>] [--candidate-log <file>] [--baseline-log <file>] [--transaction <dir>] [--timeout <seconds>] [--output <file>] [--database <db>]");
                        return 2;
                    }
                    var gameTarget = verifyPositionalGame ?? (verifyOptions.TryGetValue("--game", out var g) ? g : null);
                    if (string.IsNullOrWhiteSpace(gameTarget))
                    {
                        Console.Error.WriteLine("Error: verify requires a target game file or folder (via argument or --game).");
                        return 2;
                    }
                    var scenarioType = ScenarioType.Boot;
                    if (verifyOptions.TryGetValue("--scenario", out var scenarioWireName))
                    {
                        try { scenarioType = ScenarioDefinition.ParseWireName(scenarioWireName); }
                        catch { Console.Error.WriteLine($"Unrecognized scenario '{scenarioWireName}'."); return 2; }
                    }
                    var scenarioDef = ScenarioDefinition.CreateBuiltIn(scenarioType);
                    if (verifyOptions.TryGetValue("--timeout", out var timeoutStr))
                    {
                        if (!int.TryParse(timeoutStr, out var timeoutSec) || timeoutSec <= 0)
                        {
                            Console.Error.WriteLine("Timeout must be a positive integer in seconds.");
                            return 2;
                        }
                        scenarioDef = scenarioDef with { Timeout = TimeSpan.FromSeconds(timeoutSec) };
                    }
                    verifyOptions.TryGetValue("--kyty", out var verifyKyty);
                    verifyOptions.TryGetValue("--transaction", out var verifyTx);
                    verifyOptions.TryGetValue("--baseline-log", out var verifyBaselineLog);
                    verifyOptions.TryGetValue("--candidate-log", out var verifyCandidateLog);

                    var verifyResult = await ServiceFactory.CreateVerificationService().VerifyAsync(
                        scenarioDef,
                        gameTarget,
                        verifyKyty,
                        verifyTx,
                        verifyBaselineLog,
                        verifyCandidateLog,
                        token);

                    if (verifyOptions.TryGetValue("--output", out var verifyOutput))
                    {
                        await WriteNewJsonAsync(verifyOutput, verifyResult, token);
                    }
                    if (verifyOptions.TryGetValue("--database", out var verifyDb))
                    {
                        var dbStore = new SqliteKnowledgeStore(verifyDb);
                        await dbStore.InitializeAsync(token);
                        await dbStore.SaveVerificationAsync(verifyResult.Id, verifyResult, token);
                        Console.Error.WriteLine($"Saved verification: {verifyResult.Id}");
                    }
                    result = verifyResult;
                    break;
                default:
                    if (args.Length < 2 || args[0] is not ("analyze" or "diagnose" or "generate-prompt"))
                    {
                        Console.Error.WriteLine(Help);
                        return 2;
                    }
                    var explicitReport = args[0] == "generate-prompt" && args[1] == "--report";
                    var savedPrompt = explicitReport || args[0] == "generate-prompt" && args[1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                    if (explicitReport && args.Length < 3) { Console.Error.WriteLine("--report needs a research JSON file."); return 2; }
                    var options = new Dictionary<string, string>();
                    for (var i = explicitReport ? 3 : 2; i < args.Length; i += 2)
                    {
                        var allowed = args[0] == "generate-prompt"
                            ? savedPrompt ? new[] { "--mode", "--output", "--bundle" } : ["--kyty", "--log", "--database", "--knowledge", "--related", "--mode", "--output", "--bundle"]
                            : args[0] == "analyze" ? new[] { "--kyty", "--log", "--database" } : ["--kyty", "--log", "--database", "--knowledge", "--related"];
                        if (i + 1 >= args.Length || !allowed.Contains(args[i], StringComparer.Ordinal) || !options.TryAdd(args[i], args[i + 1]))
                        {
                            Console.Error.WriteLine("Invalid, duplicate or incomplete command option.");
                            return 2;
                        }
                    }
                    options.TryGetValue("--kyty", out var kyty);
                    options.TryGetValue("--log", out var logPath);
                    if (args[0] is "diagnose" or "generate-prompt")
                    {
                        var mode = PromptMode.Diagnose;
                        if (options.TryGetValue("--mode", out var requestedMode) && (!Enum.TryParse(requestedMode, true, out mode) || !Enum.IsDefined(mode) || int.TryParse(requestedMode, out _))) { Console.Error.WriteLine("Unrecognized prompt mode."); return 2; }
                        if (options.ContainsKey("--knowledge") && options.ContainsKey("--database")) { Console.Error.WriteLine("Use --knowledge or --database, not both."); return 2; }
                        SqliteKnowledgeStore? researchStore = null;
                        KnowledgeBundle? knowledge = null;
                        if (options.TryGetValue("--knowledge", out var knowledgePath)) { knowledge = await KnowledgeJson.ReadAsync(knowledgePath, token); }
                        if (options.TryGetValue("--database", out var dbPath))
                        {
                            researchStore = new SqliteKnowledgeStore(dbPath); await researchStore.InitializeAsync(token); knowledge = await researchStore.ExportKnowledgeAsync(token);
                        }
                        options.TryGetValue("--related", out var relatedPath);
                        var research = savedPrompt ? await ResearchJson.ReadAsync(args[explicitReport ? 2 : 1], token) : await ServiceFactory.CreateResearchService().ResearchAsync(args[1], kyty, logPath, knowledge, relatedPath, token);
                        if (researchStore is not null)
                        {
                            var researchId = Guid.NewGuid().ToString("N"); await researchStore.SaveResearchAsync(researchId, research, token);
                            Console.Error.WriteLine($"Saved research: {researchId}");
                        }
                        if (args[0] == "generate-prompt")
                        {
                            var prompt = new PromptGenerator().Generate(research, mode);
                            if (options.TryGetValue("--output", out var promptOutput)) { await PromptExporter.WriteTextAsync(prompt, promptOutput, token); }
                            if (options.TryGetValue("--bundle", out var packageOutput)) { await PromptExporter.WritePackageAsync(research, prompt, packageOutput, token); }
                            result = prompt;
                        }
                        else { result = research; }
                        break;
                    }
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
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException or RegexMatchTimeoutException or Microsoft.Data.Sqlite.SqliteException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 3;
        }
    }

    private static Dictionary<string, string>? ParseOptions(string[] arguments, params string[] allowed)
    {
        if (arguments.Length % 2 != 0) { return null; }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Length; i += 2)
        {
            if (!allowed.Contains(arguments[i], StringComparer.Ordinal) || !result.TryAdd(arguments[i], arguments[i + 1])) { return null; }
        }
        return result;
    }

    private static async Task<PatchProposal> RequirePatchAsync(string path, CancellationToken token)
    {
        var imported = await ServiceFactory.CreatePatchImporter().ImportAsync(path, token);
        return imported.Status == PatchImportStatus.Normalized && imported.Patch is not null ? imported.Patch : throw new InvalidDataException("PATCH REJECTED: input did not normalize to versioned PatchProposal JSON.");
    }

    private static async Task WriteNewJsonAsync<T>(string path, T value, CancellationToken token)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(output, value, JsonData.Options, token);
        await output.FlushAsync(token);
    }
}
