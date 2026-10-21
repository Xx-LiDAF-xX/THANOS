using System.Text.Json;
using Thanos.Core;
using Thanos.Infrastructure;
using Xunit;

namespace Thanos.Tests;

public class CliVerificationTests
{
    private static async Task<string> RunCliAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var entryPoint = typeof(Thanos.Cli.Program).Assembly.EntryPoint!;
            var result = entryPoint.Invoke(null, [args]);
            if (result is Task<int> task)
            {
                var exitCode = await task;
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"CLI exited with code {exitCode}: {stderr}");
                }
            }
            return stdout.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task VerifyCommand_OfflineLogs_EmitsValidVerificationResultJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "thanos-cli-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var gamePath = Path.Combine(tempDir, "eboot.bin");
            await File.WriteAllBytesAsync(gamePath, new byte[64]);

            var candidateLog = Path.Combine(tempDir, "candidate.log");
            await File.WriteAllTextAsync(candidateLog, "[INFO] runtimeLinker: mapping program\n[INFO] guest execution\n");

            var outputJson = await RunCliAsync("verify", gamePath, "--scenario", "BOOT", "--candidate-log", candidateLog);

            var result = JsonData.Deserialize<VerificationResult>(outputJson);
            Assert.Equal(1, result.SchemaVersion);
            Assert.Equal("BOOT", result.Scenario);
            Assert.Equal(TestOutcome.Pass, result.Outcome);
            Assert.Equal((int)VerificationLevel.Boot, result.Level);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerifyCommand_Help_IncludesVerifyUsage()
    {
        var output = await RunCliAsync("help");
        Assert.Contains("kytyfixer verify", output);
        Assert.Contains("0.5.0", output);
    }
}
