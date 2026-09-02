using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Handoff.Domain;
using Xunit;

namespace Neura.Tests;

public class HandoffValidatorTests
{
    [Fact]
    public void Validate_ReturnsInsufficient_WhenRemainingWorkMissingAndNotCompleted()
    {
        var package = ContextHandoffPackage.Build(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Mission", "Task", "InProgress",
            completedWork: new(), remainingWork: new(), decisions: new(),
            constraints: new(), filesChanged: new(), errors: new(),
            tests: new(), dependencies: new(), openQuestions: new(), relevantMemory: new());

        var result = HandoffValidator.Validate(package);

        Assert.False(result.IsSufficient);
        Assert.Contains("remainingWork", result.MissingItems);
    }

    [Fact]
    public void Validate_ReturnsSufficient_WhenCoreFieldsPresent()
    {
        var package = ContextHandoffPackage.Build(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Mission", "Task", "InProgress",
            completedWork: new() { "step 1" }, remainingWork: new() { "step 2" }, decisions: new(),
            constraints: new(), filesChanged: new(), errors: new(),
            tests: new(), dependencies: new(), openQuestions: new(), relevantMemory: new());

        var result = HandoffValidator.Validate(package);

        Assert.True(result.IsSufficient);
        Assert.Empty(result.MissingItems);
    }
}
