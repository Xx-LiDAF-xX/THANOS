using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Thanos.Infrastructure;

/// <summary>Read selected members into an anonymous seekable spool; never extract archive paths to disk.</summary>
public static class ContainerInputs
{
    public static bool IsArchive(string path) => new[] { ".zip", ".rar", ".7z", ".zar", ".tar", ".tgz", ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst" }
        .Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static (string Container, string? Member) Split(string path)
    {
        var marker = path.IndexOf("!/", StringComparison.Ordinal);
        if (marker < 0) { return (path, null); }
        var container = path[..marker];
        if (!IsArchive(container)) { throw new InvalidDataException("Member selection requires a supported archive: archive.zip!/path/to/file."); }
        var member = path[(marker + 2)..];
        ValidateMember(member);
        return (container, member);
    }

    public static void ValidateMember(string member)
    {
        member = TrimCurrentDirectory(member);
        if (string.IsNullOrWhiteSpace(member) || member.StartsWith('/') || member.Contains('\\') || member.Contains(':')
            || member.Any(char.IsControl) || member.IndexOfAny(['*', '?', '[', ']']) >= 0
            || member.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException("Archive member must be a plain relative path without traversal, control characters or wildcards.");
        }
    }

    private static string TrimCurrentDirectory(string name)
    {
        while (name.StartsWith("./", StringComparison.Ordinal)) { name = name[2..]; }
        return name;
    }

    public static async Task<string[]> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        string[] names;
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            names = archive.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e =>
            {
                if (((e.ExternalAttributes >> 16) & 0xf000) is not (0 or 0x8000)) { throw new InvalidDataException("Archive contains a non-regular file member."); }
                return e.FullName;
            }).ToArray();
        }
        else if (path.EndsWith(".zar", StringComparison.OrdinalIgnoreCase))
        {
            var json = await CaptureAsync(Python(), [HelperPath, "list", path], cancellationToken);
            names = JsonSerializer.Deserialize<string[]>(json) ?? throw new InvalidDataException("Invalid ZArchive member inventory.");
        }
        else
        {
            var tar = await BsdtarAsync(cancellationToken);
            var listing = await CaptureAsync(tar, ["-tf", path], cancellationToken);
            var detailed = await CaptureAsync(tar, ["-tvf", path], cancellationToken);
            var all = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('\r')).ToArray();
            var types = detailed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (all.Length != types.Length || types.Any(s => s.Length == 0 || s[0] is not ('-' or 'd')))
            {
                throw new InvalidDataException("Archive inventory is ambiguous or contains links/special files.");
            }
            names = all.Where((_, i) => types[i][0] == '-').ToArray();
        }
        foreach (var name in names) { cancellationToken.ThrowIfCancellationRequested(); ValidateMember(name); }
        if (names.Select(TrimCurrentDirectory).Distinct(StringComparer.Ordinal).Count() != names.Length) { throw new InvalidDataException("Archive has duplicate member names; select a non-ambiguous archive."); }
        return names;
    }

    public static async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var (container, member) = Split(path);
        if (member is null) { return new FileStream(container, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan); }
        var names = await ListAsync(container, cancellationToken);
        if (!names.Contains(member, StringComparer.Ordinal)) { throw new FileNotFoundException("Archive member not found.", path); }
        var spool = new FileStream(Path.Combine(Path.GetTempPath(), "thanos-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            if (container.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(container);
                var entry = archive.Entries.Single(e => e.FullName == member);
                await using var source = entry.Open();
                await source.CopyToAsync(spool, cancellationToken);
                if (spool.Length != entry.Length) { throw new InvalidDataException("ZIP member length does not match its directory record."); }
            }
            else if (container.EndsWith(".zar", StringComparison.OrdinalIgnoreCase))
            {
                await RunAsync(Python(), [HelperPath, "read", Path.GetFullPath(container), member], spool, cancellationToken);
            }
            else
            {
                await RunAsync(await BsdtarAsync(cancellationToken), ["-xOf", Path.GetFullPath(container), "--", member], spool, cancellationToken);
            }
            spool.Position = 0;
            return spool;
        }
        catch { await spool.DisposeAsync(); throw; }
    }

    private static string HelperPath => Path.Combine(AppContext.BaseDirectory, "InputHelpers", "zarchive_reader.py");
    private static string Python() => Environment.GetEnvironmentVariable("THANOS_PYTHON") ?? (OperatingSystem.IsWindows() ? "python" : "python3");
    private static async Task<string> BsdtarAsync(CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable("THANOS_BSDTAR");
        foreach (var candidate in configured is null ? new[] { "bsdtar", "tar" } : [configured])
        {
            try
            {
                if ((await CaptureAsync(candidate, ["--version"], cancellationToken)).Contains("bsdtar", StringComparison.OrdinalIgnoreCase)) { return candidate; }
            }
            catch (IOException) { }
        }
        throw new InvalidDataException("RAR/7z/tar inputs require libarchive bsdtar. Install it and set THANOS_BSDTAR to its executable path; ZIP needs no helper.");
    }
    private static async Task<string> CaptureAsync(string executable, string[] args, CancellationToken token)
    {
        using var output = new MemoryStream();
        await RunAsync(executable, args, output, token);
        return new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
    private static async Task RunAsync(string executable, string[] args, Stream output, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) { info.ArgumentList.Add(arg); }
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Win32Exception ex) { throw new IOException($"Cannot start input helper '{executable}'. ZAR requires Python 3.14 or Python with zstandard; set THANOS_PYTHON if needed.", ex); }
        using var registration = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, token);
            await process.WaitForExitAsync(token);
            var error = await errors;
            if (process.ExitCode != 0) { throw new InvalidDataException($"Input helper failed ({process.ExitCode}): {error.Trim()}"); }
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
    }
}
