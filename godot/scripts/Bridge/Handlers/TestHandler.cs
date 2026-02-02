// Test Handler - Handles ping/pong test messages
//
// Used for IPC integration testing between frontend and backend.

using System;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for test messages (ping/pong).
/// Used to verify IPC communication is working.
/// </summary>
public sealed class TestHandler : IMessageHandler
{
    private readonly IAppLogger _logger;

    public string MessageType => MessageTypes.Test;

    public TestHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string action)
    {
        return action is "ping" or "echo";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Debug("TestHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "ping" => HandlePing(message),
            "echo" => HandleEcho(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandlePing(IpcMessage message)
    {
        _logger.Info("Received ping, sending pong");

        var response = new IpcMessage(
            MessageTypes.Test,
            "pong",
            new
            {
                receivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                originalPayload = message.Payload,
            },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleEcho(IpcMessage message)
    {
        _logger.Info("Received echo request");

        var response = new IpcMessage(
            MessageTypes.Test,
            "echo",
            message.Payload,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }
}
