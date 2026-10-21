using Thanos.Core;
using Thanos.Desktop.ViewModels;
using Thanos.Infrastructure;
using Xunit;

namespace Thanos.Tests;

public class DesktopVerificationTests
{
    [Fact]
    public void MainViewModel_InitializesWithVerificationDefaults()
    {
        var vm = new MainViewModel(ServiceFactory.CreateResearchService());

        Assert.NotEmpty(vm.Scenarios);
        Assert.Equal(ScenarioType.Boot, vm.SelectedScenario);
        Assert.False(vm.HasVerification);
        Assert.NotNull(vm.VerifyCommand);
    }

    [Fact]
    public async Task MainViewModel_VerifyCommand_OfflineRun_PopulatesVerificationResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "thanos-vm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var gamePath = Path.Combine(tempDir, "eboot.bin");
            await File.WriteAllBytesAsync(gamePath, new byte[64]);

            var candidateLog = Path.Combine(tempDir, "candidate.log");
            await File.WriteAllTextAsync(candidateLog, "[INFO] runtimeLinker: mapping program\n[INFO] guest execution\n");

            var vm = new MainViewModel(ServiceFactory.CreateResearchService())
            {
                GamePath = gamePath,
                LogPath = candidateLog,
                SelectedScenario = ScenarioType.Boot
            };

            Assert.True(vm.VerifyCommand.CanExecute(null));
            vm.VerifyCommand.Execute(null);

            // Allow async command execution to complete
            for (int i = 0; i < 50 && (!vm.HasVerification || string.IsNullOrEmpty(vm.VerificationOutput)); i++)
            {
                await Task.Delay(50);
            }

            Assert.True(vm.HasVerification);
            Assert.NotNull(vm.CurrentVerification);
            Assert.Equal(TestOutcome.Pass, vm.CurrentVerification.Outcome);
            Assert.Equal((int)VerificationLevel.Boot, vm.CurrentVerification.Level);
            Assert.Contains("\"scenario\": \"BOOT\"", vm.VerificationOutput);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
