namespace Claupet.Tests;

public class ModelChoiceTests
{
    [Theory]
    [InlineData("opus", "Opus 5.5")]
    [InlineData("opus[1m]", "Opus 5.5 (1M)")]
    [InlineData("sonnet", "Sonnet 5.5")]
    [InlineData("sonnet[1m]", "Sonnet 5.5 (1M)")]
    [InlineData("fable", "Fable 5.1")]
    [InlineData("haiku", "Haiku 5.5")]
    [InlineData("opusplan", "Opus 5.5 + Sonnet 5.5 (plan)")]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("gpt-5", "gpt-5")]
    public void Los_alias_se_ven_con_su_version(string choice, string label) =>
        Assert.Equal(label, Format.ModelChoice(choice));
}
