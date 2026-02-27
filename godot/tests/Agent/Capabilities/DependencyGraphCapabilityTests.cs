using FluentAssertions;
using System.Threading;
using NSubstitute;
using UAssetViewer.Agent.Capabilities;
using Xunit;

namespace UAssetViewer.Tests.Agent.Capabilities;

public sealed class DependencyGraphCapabilityTests
{
    [Fact]
    public void GetStats_WithNoOpenProject_ReturnsNonExistent()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns((string?)null);

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        var capability = new DependencyGraphCapability(provider, dataAccess, new TestLogger());

        // Act
        var stats = capability.GetStats();

        // Assert
        stats.Exists.Should().BeFalse();
    }

    [Fact]
    public void GetDependencies_ClampsRequestedLimit()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns("/tmp/project");

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        dataAccess.GetDependencies("/tmp/project", "Game/A.uasset", 1000).Returns(new[]
        {
            new DependencyEdge("Game/B.uasset", "StaticMesh"),
        });

        var capability = new DependencyGraphCapability(provider, dataAccess, new TestLogger());

        // Act
        var result = capability.GetDependencies("Game/A.uasset", limit: 50000);

        // Assert
        result.Should().HaveCount(1);
        dataAccess.Received(1).GetDependencies("/tmp/project", "Game/A.uasset", 1000);
    }

    [Fact]
    public void SearchProperties_ForwardsValueFilter()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns("/tmp/project");

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        dataAccess.SearchProperties("/tmp/project", "Health", "100", 100).Returns(new[]
        {
            new PropertySearchHit("Game/A.uasset", "Player", "Health", "IntProperty", "100"),
        });

        var capability = new DependencyGraphCapability(provider, dataAccess, new TestLogger());

        // Act
        var result = capability.SearchProperties("Health", "100");

        // Assert
        result.Should().ContainSingle();
        result[0].PropertyName.Should().Be("Health");
    }

    [Fact]
    public void GetDependencies_WhenCancelled_Throws()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns("/tmp/project");

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        var capability = new DependencyGraphCapability(provider, dataAccess, new TestLogger());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => capability.GetDependencies("Game/A.uasset", ct: cts.Token);

        // Assert
        act.Should().Throw<OperationCanceledException>();
        dataAccess.DidNotReceiveWithAnyArgs().GetDependencies(default!, default!, default, default);
    }
}
