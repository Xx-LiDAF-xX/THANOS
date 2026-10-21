using System.Diagnostics;
using System.Text;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class StreamingProcessRunner : IStreamingProcessRunner
{
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        => RunStreamingAsync(request, cancellationToken: cancellationToken);

    public async Task<ProcessResult> RunStreamingAsync(
        ProcessRequest request,
        Action<ProcessOutputChunk>? onOutput = null,
        string? logFilePath = null,
        Action<string>? onLogLine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in request.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var kvp in request.Environment)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdoutLock)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
            try
            {
                onOutput?.Invoke(new ProcessOutputChunk(e.Data, false, DateTimeOffset.UtcNow));
            }
            catch { }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrLock)
            {
                stderrBuilder.AppendLine(e.Data);
            }
            try
            {
                onOutput?.Invoke(new ProcessOutputChunk(e.Data, true, DateTimeOffset.UtcNow));
            }
            catch { }
        };

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        Task? logMonitorTask = null;
        using var logMonitorCts = new CancellationTokenSource();

        if (!string.IsNullOrWhiteSpace(logFilePath) && onLogLine != null)
        {
            logMonitorTask = Task.Run(async () =>
            {
                long position = 0;
                while (!logMonitorCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (File.Exists(logFilePath))
                        {
                            using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            if (fs.Length > position)
                            {
                                fs.Seek(position, SeekOrigin.Begin);
                                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    try { onLogLine(line); } catch { }
                                }
                                position = fs.Position;
                            }
                        }
                    }
                    catch { }
                    try
                    {
                        await Task.Delay(50, logMonitorCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // Final drain
                try
                {
                    if (File.Exists(logFilePath))
                    {
                        using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (fs.Length > position)
                        {
                            fs.Seek(position, SeekOrigin.Begin);
                            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                            string? line;
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                try { onLogLine(line); } catch { }
                            }
                        }
                    }
                }
                catch { }
            });
        }

        bool timedOut = false;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process: {request.Executable}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
        catch (Exception)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            throw;
        }
        finally
        {
            logMonitorCts.Cancel();
            if (logMonitorTask != null)
            {
                try { await logMonitorTask; } catch { }
            }

            // Brief drain wait to ensure async standard output/error flush
            try
            {
                if (process.HasExited)
                {
                    process.WaitForExit(200);
                }
            }
            catch { }
        }

        int? exitCode = null;
        try
        {
            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
        }
        catch { }

        string stdoutText;
        lock (stdoutLock)
        {
            stdoutText = stdoutBuilder.ToString();
        }

        string stderrText;
        lock (stderrLock)
        {
            stderrText = stderrBuilder.ToString();
        }

        return new ProcessResult(exitCode, stdoutText, stderrText, timedOut);
    }
}
