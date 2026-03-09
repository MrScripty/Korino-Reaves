// IPC Dispatcher - Routes messages to appropriate handlers
//
// Central hub for IPC message routing. Receives messages from the
// Rust CefBrowserNode GDExtension via Godot signals and dispatches
// to registered handlers based on message type.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using UAssetViewer.Assets;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Diff;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge;

/// <summary>
/// Central dispatcher for IPC messages.
/// Routes incoming messages to registered handlers and sends responses.
/// </summary>
public sealed class IpcDispatcher : IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Dictionary<string, IMessageHandler> _handlers = new();
    private readonly HashSet<Task> _inFlightDispatches = new();
    private readonly object _dispatchLock = new();
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;
    private Node? _cefNode;
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
    /// Connects the dispatcher to the Rust CefBrowserNode for bidirectional communication.
    /// </summary>
    public void Connect(Node cefNode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cefNode != null && GodotObject.IsInstanceValid(_cefNode))
        {
            _cefNode.Disconnect("ipc_message_received", Callable.From<string>(OnIpcMessageReceived));
        }

        _cefNode = cefNode ?? throw new ArgumentNullException(nameof(cefNode));
        _cefNode.Connect("ipc_message_received", Callable.From<string>(OnIpcMessageReceived));

        _logger.Info("IpcDispatcher connected to CefBrowserNode");
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
        // SelectionHandler must be created before TreeHandler (TreeHandler depends on it)
        var selectionHandler = new SelectionHandler(_logger);
        RegisterHandler(selectionHandler);
        RegisterHandler(new TreeHandler(_logger, _assetManager, this, selectionHandler));
        // PropertyHandler registered separately in MainController (needs EditDatabase + dispatcher)
        RegisterHandler(new DiffHandler(_logger, _assetManager));
        RegisterHandler(new PakHandler(_logger, this));
        RegisterHandler(new ProjectHandler(_logger, this));
        RegisterHandler(new FilesystemHandler(_logger));
        RegisterHandler(new DependencyHandler(_logger, this));
    }

    /// <summary>
    /// Registers the dialog handler which requires a scene root for showing native dialogs.
    /// </summary>
    /// <param name="sceneRoot">The scene root node to attach dialogs to</param>
    public void RegisterDialogHandler(Node sceneRoot)
    {
        try
        {
            var handler = new DialogHandler(_logger, this, sceneRoot);
            RegisterHandler(handler);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to register DialogHandler");
        }
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
        _logger.Info("[DispatchAsync] Dispatching: type={Type}, action={Action}", message.Type, message.Action);

        // Log registered handlers for debugging
        _logger.Info("[DispatchAsync] Registered handlers: {Handlers}", string.Join(", ", _handlers.Keys));

        if (!_handlers.TryGetValue(message.Type, out var handler))
        {
            _logger.Warning("No handler registered for message type: {Type}", message.Type);
            SendError(message.Id, "UNKNOWN_TYPE", $"No handler for message type: {message.Type}");
            return;
        }

        _logger.Info("[DispatchAsync] Found handler: {HandlerType}", handler.GetType().Name);

        if (!handler.CanHandle(message.Action))
        {
            _logger.Warning("Handler {Type} cannot handle action: {Action}", message.Type, message.Action);
            SendError(message.Id, "UNKNOWN_ACTION", $"Unknown action: {message.Action}");
            return;
        }

        _logger.Info("[DispatchAsync] Handler can handle action, calling HandleAsync...");

        try
        {
            _logger.Info("[DispatchAsync] Calling handler.HandleAsync...");
            var response = await handler.HandleAsync(message).ConfigureAwait(false);
            _logger.Info("[DispatchAsync] HandleAsync completed, response={HasResponse}", response != null);

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
    /// Sends a message to the frontend via the Rust CefBrowserNode.
    /// </summary>
    public void Send(IpcMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cefNode == null)
        {
            _logger.Warning("Cannot send message - no CefBrowserNode connected");
            return;
        }

        _logger.Debug("Sending: type={Type}, action={Action}", message.Type, message.Action);

        var json = JsonSerializer.Serialize(message, s_jsonOptions);
        _cefNode.CallDeferred("send_ipc_message", json);
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
    /// Handles incoming IPC messages from the Rust CefBrowserNode signal.
    /// The json parameter is the raw JSON string from console.log interception.
    /// </summary>
    private void OnIpcMessageReceived(string json)
    {
        _logger.Info("[IpcDispatcher] Received raw IPC message: {Json}", json.Length > 200 ? json.Substring(0, 200) + "..." : json);

        if (!IpcMessageValidator.TryParseIncomingMessage(json, out var message, out var error))
        {
            _logger.Warning("Rejected IPC message: {Error}", error);
            return;
        }

        _logger.Info("[IpcDispatcher] Parsed message: type={Type}, action={Action}", message!.Type, message.Action);
        StartDispatch(message);
    }

    private void StartDispatch(IpcMessage message)
    {
        var task = DispatchAsync(message);

        lock (_dispatchLock)
        {
            _inFlightDispatches.Add(task);
        }

        _ = ObserveDispatchAsync(task, message);
    }

    private async Task ObserveDispatchAsync(Task task, IpcMessage message)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            _logger.Debug("Dropped IPC dispatch for {Type}/{Action} during disposal", message.Type, message.Action);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unhandled IPC dispatch failure for {Type}/{Action}", message.Type, message.Action);
        }
        finally
        {
            lock (_dispatchLock)
            {
                _inFlightDispatches.Remove(task);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_cefNode != null)
        {
            if (GodotObject.IsInstanceValid(_cefNode))
            {
                _cefNode.Disconnect("ipc_message_received", Callable.From<string>(OnIpcMessageReceived));
            }
            _cefNode = null;
        }

        _handlers.Clear();
        lock (_dispatchLock)
        {
            _inFlightDispatches.Clear();
        }
    }
}
