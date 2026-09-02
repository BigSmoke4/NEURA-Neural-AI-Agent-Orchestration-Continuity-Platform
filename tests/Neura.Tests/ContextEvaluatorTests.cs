using Neura.Modules.ContextManagement.Domain;
using Xunit;

namespace Neura.Tests;

public class ContextEvaluatorTests
{
    private readonly ContextThresholdOptions _options = new();

    [Theory]
    [InlineData(0.10, ContextLevel.Normal)]
    [InlineData(0.70, ContextLevel.Warning)]
    [InlineData(0.85, ContextLevel.Critical)]
    [InlineData(0.95, ContextLevel.Exhausted)]
    [InlineData(0.99, ContextLevel.Exhausted)]
    public void Evaluate_ClassifiesUsageAgainstConfiguredThresholds(double ratio, ContextLevel expected)
    {
        var level = ContextEvaluator.Evaluate(ratio, _options);
        Assert.Equal(expected, level);
    }
}
