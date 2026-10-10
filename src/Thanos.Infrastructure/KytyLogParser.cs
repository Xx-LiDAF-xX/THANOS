using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class KytyLogParser(IFileSystem files, IPathService paths) : ILogParser
{
    private static readonly TimeSpan RegexTimeout = Regex.InfiniteMatchTimeout;
    private static readonly Regex Pc = new(@"\b(?:PC|RIP)(?:\s*[:=]\s*|\s+)(0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);
    private static readonly Regex Opcode = new(@"\bfamily\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);
    private static readonly Regex Source = new(@"([A-Za-z0-9_./\\-]+\.(?:cpp|cc|c|h))[:(](\d+)\)?", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);
    private static readonly Regex NegatedFailure = new(@"\b(?:no|zero|0)\s+(?:(?:fatal|shader|vulkan|loader)\s+)?(?:errors?|failures?|warnings?)\b|\bno\s+unsupported\s+(?:instructions?|opcodes?|features?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);

    public async Task<ParsedLog> ParseAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = paths.GetFullPath(path);
        if (ContainerInputs.Split(fullPath).Member is null && ContainerInputs.IsArchive(fullPath))
        {
            var entries = await ContainerInputs.ListAsync(fullPath, cancellationToken);
            var candidates = entries.Where(e => e.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || e.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1) { throw new InvalidDataException("Log archive must contain one .log/.txt file; select a member explicitly with archive.zip!/path/to/log."); }
            fullPath += "!/" + candidates[0];
        }
        await using var stream = await files.OpenReadAsync(fullPath, cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var decoder = new UTF8Encoding(false, true).GetDecoder();
        var text = new StringBuilder();
        var chunk = new byte[65536];
        var characters = new char[65538];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            hash.AppendData(chunk.AsSpan(0, read));
            text.Append(characters, 0, decoder.GetChars(chunk.AsSpan(0, read), characters, flush: false));
        }
        text.Append(characters, 0, decoder.GetChars([], characters, flush: true));
        var raw = text.ToString();
        var events = new List<ErrorEvent>();
        var hardware = new Dictionary<string, string>();
        string? version = null;
        string? title = null;
        using var reader = new StringReader(raw.TrimStart('\ufeff'));
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (key.Equals("Kyty version", StringComparison.OrdinalIgnoreCase)) { version = value; }
                if (key.Equals("Title ID", StringComparison.OrdinalIgnoreCase)) { title = value; }
                if (new[] { "CPU", "GPU", "GPU driver", "RAM", "VRAM" }.Contains(key, StringComparer.OrdinalIgnoreCase)) { hardware[key.ToLowerInvariant()] = value; }
            }
            var severityText = NegatedFailure.Replace(line, "");
            var diagnosticHeader = line.Trim() is "--- Fatal Error ---" or "--- Build ---";
            var severity = diagnosticHeader ? Severity.Information : Contains(severityText, "fatal") || Contains(severityText, "assertion") ? Severity.Fatal
                : Contains(severityText, "error") || Contains(severityText, "failed") || Contains(severityText, "unsupported") || Contains(severityText, "unimplemented") || Contains(severityText, "not implemented") || Contains(severityText, "access violation") ? Severity.Error
                : Contains(severityText, "warning") || Contains(severityText, "warn") ? Severity.Warning : Severity.Information;
            var category = Category(line);
            var termination = Contains(line, "process terminated") || Contains(line, "segmentation fault") || Contains(line, "aborted");
            var source = Source.Match(line);
            var pc = Pc.Match(line);
            var opcode = Opcode.Match(line);
            events.Add(new ErrorEvent
            {
                EventType = diagnosticHeader ? "DiagnosticHeader" : termination ? "Termination" : severity == Severity.Information ? "LogLine" : category + "Error",
                Severity = termination ? Severity.Fatal : severity,
                Category = category,
                Subsystem = category.ToString(),
                Message = line,
                Opcode = opcode.Success ? opcode.Groups[1].Value : null,
                ProgramCounter = pc.Success ? new BinaryAddress(category == ErrorCategory.Shader ? AddressSpace.RelativeAddress : AddressSpace.RuntimeAddress, pc.Groups[1].Value.ToLowerInvariant()) : null,
                SourceFile = source.Success ? source.Groups[1].Value : null,
                SourceLine = source.Success && int.TryParse(source.Groups[2].Value, out var sourceLine) && sourceLine > 0 ? sourceLine : null,
                LineNumber = lineNumber,
                RawLine = line
            });
        }
        return new ParsedLog
        {
            SourcePath = fullPath,
            Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset()),
            RawText = raw,
            KytyVersion = version,
            TitleId = title,
            Hardware = hardware,
            Events = events.ToArray(),
            Limitations = ["Lexical categories and severities are heuristic, not root causes. Unknown lines are preserved. Metadata is log-reported and unverified.", "Shader PCs are relative shader addresses; host exception PCs may belong to the emulator. Neither identifies a game-file offset without additional evidence.", "No configured log size, line count or line-length limits. Full raw text/events are retained in memory; available RAM, disk and runtime object capacity determine practical capacity."]
        };
    }
    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
    private static ErrorCategory Category(string line)
    {
        if (Contains(line, "shader") || Contains(line, "family=") || Contains(line, "RDNA2")) { return ErrorCategory.Shader; }
        if (Contains(line, "vulkan") || Contains(line, "VK_ERROR")) { return ErrorCategory.Vulkan; }
        foreach (var category in new[] { ErrorCategory.Loader, ErrorCategory.Module, ErrorCategory.Syscall, ErrorCategory.Memory, ErrorCategory.Thread, ErrorCategory.Timing, ErrorCategory.Filesystem, ErrorCategory.Audio, ErrorCategory.Video, ErrorCategory.Network, ErrorCategory.Input, ErrorCategory.Presentation, ErrorCategory.Graphics, ErrorCategory.Driver, ErrorCategory.Gpu, ErrorCategory.Elf })
        {
            if (Contains(line, category.ToString())) { return category; }
        }
        return ErrorCategory.Unknown;
    }
}
