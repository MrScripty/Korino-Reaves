using FluentAssertions;
using NSubstitute;
using UAssetViewer.Agent.Capabilities;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Agent.Capabilities;

public sealed class GuiSelectionCapabilityTests
{
    [Fact]
    public void SelectNode_UpdatesStateAndBroadcasts()
    {
        // Arrange
        var controller = Substitute.For<ISelectionStateController>();
        var broadcaster = Substitute.For<ISelectionBroadcaster>();
        var nextState = new SelectionState("file:Game/A.uasset", new[] { "folder:Game" });

        controller.SelectNode("file:Game/A.uasset").Returns(nextState);

        var capability = new GuiSelectionCapability(controller, broadcaster, new TestLogger());

        // Act
        var state = capability.SelectNode("file:Game/A.uasset");

        // Assert
        state.Should().Be(nextState);
        broadcaster.Received(1).Broadcast(nextState);
    }

    [Fact]
    public void ExpandNodes_WithEmptyArray_DoesNotBroadcast()
    {
        // Arrange
        var currentState = new SelectionState(null, System.Array.Empty<string>());
        var controller = Substitute.For<ISelectionStateController>();
        controller.CurrentState.Returns(currentState);
        var broadcaster = Substitute.For<ISelectionBroadcaster>();

        var capability = new GuiSelectionCapability(controller, broadcaster, new TestLogger());

        // Act
        var state = capability.ExpandNodes(System.Array.Empty<string>());

        // Assert
        state.Should().Be(currentState);
        broadcaster.DidNotReceive().Broadcast(Arg.Any<SelectionState>());
    }

    [Fact]
    public void CollapseAll_BroadcastsUpdatedState()
    {
        // Arrange
        var controller = Substitute.For<ISelectionStateController>();
        var broadcaster = Substitute.For<ISelectionBroadcaster>();
        var collapsed = new SelectionState(null, System.Array.Empty<string>());
        controller.CollapseAll().Returns(collapsed);

        var capability = new GuiSelectionCapability(controller, broadcaster, new TestLogger());

        // Act
        var state = capability.CollapseAll();

        // Assert
        state.Should().Be(collapsed);
        broadcaster.Received(1).Broadcast(collapsed);
    }
}
