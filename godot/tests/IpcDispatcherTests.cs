// Unit tests for IpcDispatcher

using System;
using System.Threading.Tasks;
using FluentAssertions;
using UAssetViewer.Assets;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests;

public class IpcDispatcherTests : IDisposable
{
    private readonly TestLogger _logger;
    private readonly IpcDispatcher _dispatcher;

    public IpcDispatcherTests()
    {
        _logger = new TestLogger();
        _dispatcher = new IpcDispatcher(_logger, new AssetManager(_logger));
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    [Fact]
    public void RegisterHandler_ShouldAddHandler()
    {
        // Arrange
        var handler = new TestHandler(_logger);

        // Act
        _dispatcher.RegisterHandler(handler);

        // Assert
        var retrieved = _dispatcher.GetHandler<TestHandler>();
        retrieved.Should().NotBeNull();
        retrieved.Should().BeSameAs(handler);
    }

    [Fact]
    public void RegisterHandler_ShouldReplaceExistingHandler()
    {
        // Arrange
        var handler1 = new TestHandler(_logger);
        var handler2 = new TestHandler(_logger);

        // Act
        _dispatcher.RegisterHandler(handler1);
        _dispatcher.RegisterHandler(handler2);

        // Assert
        var retrieved = _dispatcher.GetHandler<TestHandler>();
        retrieved.Should().BeSameAs(handler2);
    }

    [Fact]
    public async Task DispatchAsync_WithUnknownType_ShouldLogWarning()
    {
        // Arrange
        var message = new IpcMessage("unknown", "action", null, "id-1");

        // Act
        await _dispatcher.DispatchAsync(message);

        // Assert
        _logger.Entries.Should().Contain(e =>
            e.Level == Infrastructure.LogLevel.Warning &&
            e.Message.Contains("No handler registered for message type")
        );
    }

    [Fact]
    public async Task DispatchAsync_WithPingAction_ShouldCallHandler()
    {
        // Arrange
        _dispatcher.RegisterHandler(new TestHandler(_logger));
        var message = new IpcMessage(MessageTypes.Test, "ping", new { timestamp = 12345 }, "id-1");

        // Act
        await _dispatcher.DispatchAsync(message);

        // Assert
        _logger.Entries.Should().Contain(e =>
            e.Level == Infrastructure.LogLevel.Info &&
            e.Message.Contains("ping")
        );
    }

    [Fact]
    public void GetHandler_WithUnregisteredType_ShouldReturnNull()
    {
        // Act
        var result = _dispatcher.GetHandler<TestHandler>();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Dispose_ShouldPreventFurtherOperations()
    {
        // Arrange
        _dispatcher.Dispose();

        // Act & Assert
        var act = () => _dispatcher.RegisterHandler(new TestHandler(_logger));
        act.Should().Throw<ObjectDisposedException>();
    }
}

public class TestHandlerTests : IDisposable
{
    private readonly TestLogger _logger;
    private readonly TestHandler _handler;

    public TestHandlerTests()
    {
        _logger = new TestLogger();
        _handler = new TestHandler(_logger);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    [Fact]
    public void MessageType_ShouldBeTest()
    {
        // Assert
        _handler.MessageType.Should().Be(MessageTypes.Test);
    }

    [Theory]
    [InlineData("ping", true)]
    [InlineData("echo", true)]
    [InlineData("unknown", false)]
    [InlineData("pong", false)]
    public void CanHandle_ShouldReturnCorrectValue(string action, bool expected)
    {
        // Act
        var result = _handler.CanHandle(action);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task HandleAsync_Ping_ShouldReturnPong()
    {
        // Arrange
        var message = new IpcMessage(MessageTypes.Test, "ping", new { timestamp = 12345 }, "id-1");

        // Act
        var response = await _handler.HandleAsync(message);

        // Assert
        response.Should().NotBeNull();
        response!.Type.Should().Be(MessageTypes.Test);
        response.Action.Should().Be("pong");
        response.Id.Should().Be("id-1");
    }

    [Fact]
    public async Task HandleAsync_Echo_ShouldReturnSamePayload()
    {
        // Arrange
        var payload = new { data = "test data" };
        var message = new IpcMessage(MessageTypes.Test, "echo", payload, "id-2");

        // Act
        var response = await _handler.HandleAsync(message);

        // Assert
        response.Should().NotBeNull();
        response!.Type.Should().Be(MessageTypes.Test);
        response.Action.Should().Be("echo");
        response.Payload.Should().Be(payload);
        response.Id.Should().Be("id-2");
    }

    [Fact]
    public async Task HandleAsync_UnknownAction_ShouldReturnNull()
    {
        // Arrange
        var message = new IpcMessage(MessageTypes.Test, "unknown", null, "id-3");

        // Act
        var response = await _handler.HandleAsync(message);

        // Assert
        response.Should().BeNull();
    }
}
