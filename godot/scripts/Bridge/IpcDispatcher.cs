// IPC Dispatcher - Routes messages to appropriate handlers
//
// Central hub for IPC message routing. Receives messages from CEF
// and dispatches to registered handlers based on message type.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UAssetViewer.Assets;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Cef;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge;

/// <summary>
/// Central dispatcher for IPC messages.
/// Routes incoming messages to registered handlers and sends responses.
/// </summary>
public sealed class IpcDispatcher : IDisposable
{
    private readonly Dictionary<string, IMessageHandler> _handlers = new();
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;
    private CefBrowserWrapper? _browser;
    private bool _disposed;

    public IpcDispatcher(IAppLogger logger, AssetManager assetManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    }

    /// <summary>
    /// Gets the AssetManager instance.
    /// </summary>
    public AssetManager AssetManager => _assetManager;

    /// <summary>
    /// Connects the dispatcher to a browser for bidirectional communication.
    /// </summary>
    public void Connect(CefBrowserWrapper browser)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_browser != null)
        {
            _browser.MessageReceived -= OnMessageReceived;
        }

        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _browser.MessageReceived += OnMessageReceived;

        _logger.Info("IpcDispatcher connected to browser");
    }

    /// <summary>
    /// Registers a message handler.
    /// </summary>
    /// <param name="handler">Handler to register</param>
    public void RegisterHandler(IMessageHandler handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentNullException.ThrowIfNull(handler);

        if (_handlers.ContainsKey(handler.MessageType))
        {
            _logger.Warning("Replacing existing handler for type: {Type}", handler.MessageType);
        }

        _handlers[handler.MessageType] = handler;
        _logger.Debug("Registered handler for type: {Type}", handler.MessageType);
    }

    /// <summary>
    /// Registers all default handlers.
    /// </summary>
    public void RegisterDefaultHandlers()
    {
        RegisterHandler(new TestHandler(_logger));
        RegisterHandler(new AssetHandler(_logger, _assetManager));
        RegisterHandler(new TreeHandler(_logger, _assetManager));
        RegisterHandler(new PropertyHandler(_logger, _assetManager));
        RegisterHandler(new SelectionHandler(_logger));
    }

    /// <summary>
    /// Gets a registered handler by type.
    /// </summary>
    public T? GetHandler<T>() where T : class, IMessageHandler
    {
        foreach (var handler in _handlers.Values)
        {
            if (handler is T typed)
            {
                return typed;
            }
        }
        return null;
    }

    /// <summary>
    /// Dispatches a message to the appropriate handler.
    /// </summary>
    public async Task DispatchAsync(IpcMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var scope = _logger.BeginScope($"Dispatch:{message.Type}:{message.Action}");

        if (!_handlers.TryGetValue(message.Type, out var handler))
        {
            _logger.Warning("No handler registered for message type: {Type}", message.Type);
            SendError(message.Id, "UNKNOWN_TYPE", $"No handler for message type: {message.Type}");
            return;
        }

        if (!handler.CanHandle(message.Action))
        {
            _logger.Warning("Handler {Type} cannot handle action: {Action}", message.Type, message.Action);
            SendError(message.Id, "UNKNOWN_ACTION", $"Unknown action: {message.Action}");
            return;
        }

        try
        {
            var response = await handler.HandleAsync(message).ConfigureAwait(false);

            if (response != null)
            {
                Send(response);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling message: {Type}/{Action}", message.Type, message.Action);
            SendError(message.Id, "HANDLER_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Sends a message to the frontend.
    /// </summary>
    public void Send(IpcMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_browser == null)
        {
            _logger.Warning("Cannot send message - no browser connected");
            return;
        }

        _logger.Debug("Sending: type={Type}, action={Action}", message.Type, message.Action);
        _browser.SendMessage(message);
    }

    /// <summary>
    /// Sends a message to the frontend.
    /// </summary>
    public void Send(string type, string action, object? payload, string? id = null)
    {
        var message = new IpcMessage(
            type,
            action,
            payload,
            id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        Send(message);
    }

    /// <summary>
    /// Sends an error response to the frontend.
    /// </summary>
    public void SendError(string? correlationId, string code, string message)
    {
        var error = new ErrorResponse(code, message, null);
        Send(MessageTypes.Error, "error", error, correlationId);
    }

    /// <summary>
    /// Handles incoming messages from the browser.
    /// </summary>
    private void OnMessageReceived(IpcMessage message)
    {
        // Fire and forget - dispatch asynchronously
        _ = DispatchAsync(message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser != null)
        {
            _browser.MessageReceived -= OnMessageReceived;
            _browser = null;
        }

        _handlers.Clear();
    }
}
