using FluentAssertions;
using UAssetViewer.Agent;
using Xunit;

namespace UAssetViewer.Tests.Agent;

public sealed class AgentExecutionPolicyTests
{
    [Fact]
    public void ReadOnlyDefault_DisablesPersistentWrites()
    {
        // Act
        var policy = AgentExecutionPolicy.ReadOnlyDefault;

        // Assert
        policy.AllowAssetWriteOperations.Should().BeFalse();
        policy.AllowPropertyEdits.Should().BeFalse();
        policy.AllowModelDownloads.Should().BeFalse();
        policy.AllowGuiMutation.Should().BeTrue();
    }

    [Fact]
    public void EnsureAssetWritesAllowed_WhenReadOnly_ThrowsPolicyViolation()
    {
        // Arrange
        var policy = AgentExecutionPolicy.ReadOnlyDefault;

        // Act
        var act = () => policy.EnsureAssetWritesAllowed("save_asset");

        // Assert
        act.Should().Throw<AgentPolicyViolationException>()
            .WithMessage("*save_asset*");
    }

    [Fact]
    public void ClampLimits_UseConfiguredMaximums()
    {
        // Arrange
        var policy = AgentExecutionPolicy.ReadOnlyDefault with
        {
            MaxProjectSearchResults = 25,
            MaxDependencyQueryResults = 50,
            MaxDependencyRelatedResults = 60,
            MaxDependencyTraversalDepth = 4,
            MaxMetadataRows = 80
        };

        // Act / Assert
        policy.ClampProjectSearchLimit(999, fallback: 100).Should().Be(25);
        policy.ClampDependencyQueryLimit(999, fallback: 100).Should().Be(50);
        policy.ClampDependencyRelatedLimit(999, fallback: 200).Should().Be(60);
        policy.ClampDependencyTraversalDepth(999, fallback: 3).Should().Be(4);
        policy.ClampMetadataRowLimit(999, fallback: 200).Should().Be(80);
    }
}
