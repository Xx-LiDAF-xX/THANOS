using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class KytyLogParser(IFileSystem files, IPathService paths) : ILogParser
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public const int MaximumLineLength = 65536;
    public const int MaximumLines = 100000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Pc = new(@"\b(?:PC|RIP)\s*[:=]\s*(0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Opcode = new(@"\bfamily\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Source = new(@"([A-Za-z0-9_./\\-]+\.(?:cpp|cc|c|h))[:(](\d+)\)?", RegexOptions.CultureInvariant, RegexTimeout);

    public async Task<ParsedLog> ParseAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = paths.GetFullPath(path);
        await using var stream = files.OpenRead(fullPath);
        if (stream.Length > MaximumBytes)
        {
            throw new InvalidDataException("Log exceeds the 16 MiB input limit. Select a smaller log.");
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > MaximumBytes)
            {
                throw new InvalidDataException("Log grew beyond the 16 MiB input limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        var bytes = buffer.ToArray();
        var raw = new UTF8Encoding(false, true).GetString(bytes);
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
            if (lineNumber > MaximumLines)
            {
                throw new InvalidDataException("Log exceeds the 100000 line limit.");
            }
            if (line.Length > MaximumLineLength)
            {
                throw new InvalidDataException($"Log line {lineNumber} exceeds 65536 characters.");
            }
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (key.Equals("Kyty version", StringComparison.OrdinalIgnoreCase)) { version = value; }
                if (key.Equals("Title ID", StringComparison.OrdinalIgnoreCase)) { title = value; }
                if (new[] { "CPU", "GPU", "GPU driver", "RAM", "VRAM" }.Contains(key, StringComparer.OrdinalIgnoreCase)) { hardware[key.ToLowerInvariant()] = value; }
            }
            var severity = Contains(line, "fatal") || Contains(line, "assertion") ? Severity.Fatal
                : Contains(line, "error") || Contains(line, "failed") || Contains(line, "unsupported") ? Severity.Error
                : Contains(line, "warning") || Contains(line, "warn") ? Severity.Warning : Severity.Information;
            var category = Category(line);
            var source = Source.Match(line);
            var pc = Pc.Match(line);
            var opcode = Opcode.Match(line);
            events.Add(new ErrorEvent
            {
                EventType = severity == Severity.Information ? "LogLine" : category + "Error",
                Severity = severity,
                Category = category,
                Subsystem = category.ToString(),
                Message = line,
                Opcode = opcode.Success ? opcode.Groups[1].Value : null,
                ProgramCounter = pc.Success ? new BinaryAddress(AddressSpace.RuntimeAddress, pc.Groups[1].Value) : null,
                SourceFile = source.Success ? source.Groups[1].Value : null,
                SourceLine = source.Success && int.TryParse(source.Groups[2].Value, out var sourceLine) && sourceLine > 0 ? sourceLine : null,
                LineNumber = lineNumber,
                RawLine = line
            });
        }
        return new ParsedLog
        {
            SourcePath = fullPath,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            RawText = raw,
            KytyVersion = version,
            TitleId = title,
            Hardware = hardware,
            Events = events.ToArray(),
            Limitations = ["Initial lexical parser; categories and severities are heuristic, not root causes. Unknown lines are preserved. Metadata is log-reported and unverified."]
        };
    }
    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
    private static ErrorCategory Category(string line)
    {
        if (Contains(line, "shader") || Contains(line, "family=MIMG")) { return ErrorCategory.Shader; }
        if (Contains(line, "vulkan") || Contains(line, "VK_ERROR")) { return ErrorCategory.Vulkan; }
        foreach (var category in new[] { ErrorCategory.Loader, ErrorCategory.Module, ErrorCategory.Syscall, ErrorCategory.Memory, ErrorCategory.Thread, ErrorCategory.Timing, ErrorCategory.Filesystem, ErrorCategory.Audio, ErrorCategory.Video, ErrorCategory.Network, ErrorCategory.Input, ErrorCategory.Presentation, ErrorCategory.Graphics, ErrorCategory.Driver, ErrorCategory.Gpu, ErrorCategory.Elf })
        {
            if (Contains(line, category.ToString())) { return category; }
        }
        return ErrorCategory.Unknown;
    }
}
