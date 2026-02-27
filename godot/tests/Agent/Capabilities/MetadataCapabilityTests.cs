using FluentAssertions;
using System.Threading;
using NSubstitute;
using UAssetViewer.Agent.Capabilities;
using Xunit;

namespace UAssetViewer.Tests.Agent.Capabilities;

public sealed class MetadataCapabilityTests
{
    [Fact]
    public void GetAssetMetadata_WithNoProject_ReturnsNull()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns((string?)null);

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        var capability = new MetadataCapability(provider, dataAccess, new TestLogger());

        // Act
        var result = capability.GetAssetMetadata("Game/A.uasset");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAssetMetadata_ClampsRowLimit()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns("/tmp/project");

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        var snapshot = new AssetMetadataSnapshot(
            new AssetMetadataSummary("Game/A.uasset", "uasset", 1, 1, 1, 1),
            new[] { new MetadataImport(0, "Obj", "Class", "/Game", false) },
            new[] { new MetadataExport(0, "Obj", "Class", null, 16) },
            new[] { new MetadataProperty(0, "Obj", "Health", "IntProperty", "100", 100, null, null) },
            new[] { new MetadataEdge("Game/B.uasset", "StaticMesh") });

        dataAccess.GetAssetMetadata("/tmp/project", "Game/A.uasset", 2000).Returns(snapshot);

        var capability = new MetadataCapability(provider, dataAccess, new TestLogger());

        // Act
        var result = capability.GetAssetMetadata("Game/A.uasset", rowLimit: 999999);

        // Assert
        result.Should().NotBeNull();
        dataAccess.Received(1).GetAssetMetadata("/tmp/project", "Game/A.uasset", 2000);
    }

    [Fact]
    public void GetAssetMetadata_WhenCancelled_Throws()
    {
        // Arrange
        var provider = Substitute.For<IProjectPathProvider>();
        provider.CurrentProjectPath.Returns("/tmp/project");

        var dataAccess = Substitute.For<IDependencyDataAccess>();
        var capability = new MetadataCapability(provider, dataAccess, new TestLogger());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => capability.GetAssetMetadata("Game/A.uasset", ct: cts.Token);

        // Assert
        act.Should().Throw<OperationCanceledException>();
        dataAccess.DidNotReceiveWithAnyArgs().GetAssetMetadata(default!, default!, default, default);
    }
}
