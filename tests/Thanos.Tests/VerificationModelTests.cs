using Thanos.Core;
using Xunit;

namespace Thanos.Tests;

public class VerificationModelTests
{
    [Theory]
    [InlineData(ScenarioType.Boot, "BOOT", 1)]
    [InlineData(ScenarioType.Menu, "MENU", 3)]
    [InlineData(ScenarioType.Gameplay, "GAMEPLAY", 4)]
    [InlineData(ScenarioType.CrashReproduction, "CRASH REPRODUCTION", 1)]
    [InlineData(ScenarioType.Stability, "STABILITY", 5)]
    [InlineData(ScenarioType.Performance, "PERFORMANCE", 7)]
    public void BuiltInScenarios_HaveExpectedLevelAndWireName(ScenarioType type, string wireName, int expectedLevel)
    {
        var scenario = ScenarioDefinition.CreateBuiltIn(type);
        Assert.Equal(wireName, scenario.Name);
        Assert.Equal(type, scenario.Type);
        Assert.Equal(expectedLevel, scenario.TargetLevel);
        Assert.True(scenario.Timeout > TimeSpan.Zero);
        Assert.NotEmpty(scenario.RequiredPatterns);
    }

    [Fact]
    public void VerificationLevel_CoversZeroThroughSeven()
    {
        Assert.Equal(0, (int)VerificationLevel.None);
        Assert.Equal(1, (int)VerificationLevel.Boot);
        Assert.Equal(2, (int)VerificationLevel.Intro);
        Assert.Equal(3, (int)VerificationLevel.Menu);
        Assert.Equal(4, (int)VerificationLevel.InGame);
        Assert.Equal(5, (int)VerificationLevel.Playable);
        Assert.Equal(6, (int)VerificationLevel.Complete);
        Assert.Equal(7, (int)VerificationLevel.Perfect);
    }

    [Fact]
    public void ScenarioDefinition_FormatWireName_AndParse()
    {
        Assert.Equal("CRASH REPRODUCTION", ScenarioDefinition.ToWireName(ScenarioType.CrashReproduction));
        Assert.Equal(ScenarioType.CrashReproduction, ScenarioDefinition.ParseWireName("CRASH REPRODUCTION"));
        Assert.Equal(ScenarioType.Boot, ScenarioDefinition.ParseWireName("BOOT"));
        Assert.Throws<ArgumentException>(() => ScenarioDefinition.ParseWireName("INVALID"));
    }
}
