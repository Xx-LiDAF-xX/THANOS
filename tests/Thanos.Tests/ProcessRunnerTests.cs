using Thanos.Core;
using Thanos.Infrastructure;
using Xunit;

namespace Thanos.Tests;

public class ProcessRunnerTests
{
    private readonly IStreamingProcessRunner runner = new StreamingProcessRunner();

    private static (string Exe, string[] Args) GetEchoCommand(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("cmd.exe", ["/c", $"echo {text}"]);
        }
        return ("/bin/sh", ["-c", $"echo '{text}'"]);
    }

    private static (string Exe, string[] Args) GetSleepCommand(int seconds)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("powershell.exe", ["-NoProfile", "-Command", $"Start-Sleep -Seconds {seconds}"]);
        }
        return ("/bin/sh", ["-c", $"sleep {seconds}"]);
    }

    [Fact]
    public async Task RunStreamingAsync_CapturesOutputAndExitsCleanly()
    {
        var (exe, args) = GetEchoCommand("HelloTHANOS");
        var chunks = new List<ProcessOutputChunk>();
        var request = new ProcessRequest(exe, args, null, TimeSpan.FromSeconds(10), new Dictionary<string, string>());

        var result = await runner.RunStreamingAsync(request, chunk => chunks.Add(chunk));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("HelloTHANOS", result.StandardOutput);
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Text.Contains("HelloTHANOS") && !c.IsError);
    }

    [Fact]
    public async Task RunStreamingAsync_TimesOutAndKillsProcessTree()
    {
        var (exe, args) = GetSleepCommand(10);
        var request = new ProcessRequest(exe, args, null, TimeSpan.FromMilliseconds(500), new Dictionary<string, string>());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunStreamingAsync(request);
        stopwatch.Stop();

        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Timeout must terminate process quickly");
    }

    [Fact]
    public async Task RunStreamingAsync_CancelsCleanlyViaCancellationToken()
    {
        var (exe, args) = GetSleepCommand(10);
        var request = new ProcessRequest(exe, args, null, TimeSpan.FromSeconds(30), new Dictionary<string, string>());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunStreamingAsync(request, cancellationToken: cts.Token);
        stopwatch.Stop();

        Assert.True(result.TimedOut || cts.IsCancellationRequested);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Cancellation must terminate process quickly");
    }

    [Fact]
    public async Task RunStreamingAsync_TailsLogFileIncrementally()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var logLines = new List<string>();
            // Command writes 3 lines with small delay
            string script;
            string exe;
            string[] args;
            if (OperatingSystem.IsWindows())
            {
                exe = "powershell.exe";
                script = $"Add-Content -Path '{tempFile}' -Value 'Line 1'; Start-Sleep -Milliseconds 100; Add-Content -Path '{tempFile}' -Value 'Line 2'; Start-Sleep -Milliseconds 100; Add-Content -Path '{tempFile}' -Value 'Line 3'";
                args = ["-NoProfile", "-Command", script];
            }
            else
            {
                exe = "/bin/sh";
                script = $"echo 'Line 1' >> '{tempFile}'; sleep 0.1; echo 'Line 2' >> '{tempFile}'; sleep 0.1; echo 'Line 3' >> '{tempFile}'";
                args = ["-c", script];
            }

            var request = new ProcessRequest(exe, args, null, TimeSpan.FromSeconds(10), new Dictionary<string, string>());
            var result = await runner.RunStreamingAsync(request, logFilePath: tempFile, onLogLine: line => logLines.Add(line));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(logLines, l => l.Contains("Line 1"));
            Assert.Contains(logLines, l => l.Contains("Line 2"));
            Assert.Contains(logLines, l => l.Contains("Line 3"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
