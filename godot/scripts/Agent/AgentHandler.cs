// Agent Handler - IPC handler for AI agent operations
//
// Routes agent-related IPC messages from the frontend to the
// AgentManager and workflows. Sends progress updates back to the UI.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UAssetViewer.Agent.Workflows;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Agent;

/// <summary>
/// IPC message handler for AI agent operations.
/// </summary>
public sealed class AgentHandler : IMessageHandler
{
    private static readonly string[] SupportedActions =
    {
        "execute", "portMod", "explore", "cancel", "getStatus"
    };

    private readonly AgentManager? _agentManager;
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _currentCts;
    private string _currentStatus = AgentStatuses.Idle;
    private string _currentMessage = "";

    public AgentHandler(IAppLogger logger, AgentManager? agentManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentManager = agentManager;
    }

    /// <inheritdoc />
    public string MessageType => MessageTypes.Agent;

    /// <inheritdoc />
    public bool CanHandle(string action)
    {
        return Array.IndexOf(SupportedActions, action) >= 0;
    }

    /// <inheritdoc />
    public async Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        return message.Action switch
        {
            "execute" => await HandleExecute(message).ConfigureAwait(false),
            "portMod" => await HandlePortMod(message).ConfigureAwait(false),
            "explore" => await HandleExplore(message).ConfigureAwait(false),
            "cancel" => HandleCancel(message),
            "getStatus" => HandleGetStatus(message),
            _ => null
        };
    }

    private async Task<IpcMessage?> HandleExecute(IpcMessage message)
    {
        if (_agentManager == null)
        {
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Agent not initialized - Ollama may not be running"
            });
        }

        var payload = DeserializePayload<ExecutePayload>(message.Payload);
        if (payload?.Prompt == null)
        {
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Missing 'prompt' in payload"
            });
        }

        _currentCts = new CancellationTokenSource();
        UpdateStatus(AgentStatuses.Thinking, "Processing prompt...");

        try
        {
            var result = await _agentManager.ExecuteAsync(
                payload.Prompt, _currentCts.Token).ConfigureAwait(false);

            UpdateStatus(AgentStatuses.Complete, "Done");

            return CreateResponse(message.Id, "result", new
            {
                status = AgentStatuses.Complete,
                message = result
            });
        }
        catch (OperationCanceledException)
        {
            UpdateStatus(AgentStatuses.Idle, "Cancelled");
            return CreateResponse(message.Id, "cancelled", new
            {
                status = AgentStatuses.Idle,
                message = "Execution cancelled"
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Agent execute failed");
            UpdateStatus(AgentStatuses.Error, ex.Message);
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = ex.Message
            });
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    private async Task<IpcMessage?> HandlePortMod(IpcMessage message)
    {
        if (_agentManager == null)
        {
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Agent not initialized"
            });
        }

        var payload = DeserializePayload<PortModPayload>(message.Payload);
        if (payload == null)
        {
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Invalid portMod payload"
            });
        }

        _currentCts = new CancellationTokenSource();
        UpdateStatus(AgentStatuses.Executing, "Porting mod...");

        var workflow = new ModPortingWorkflow(_agentManager, _logger);
        var result = await workflow.ExecuteAsync(
            payload.OriginalPath,
            payload.UpdatedPath,
            payload.ModPath,
            payload.OutputPath,
            _currentCts.Token).ConfigureAwait(false);

        UpdateStatus(result.Success ? AgentStatuses.Complete : AgentStatuses.Error, result.Message);

        _currentCts?.Dispose();
        _currentCts = null;

        return CreateResponse(message.Id, "portModResult", result);
    }

    private async Task<IpcMessage?> HandleExplore(IpcMessage message)
    {
        if (_agentManager == null)
        {
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Agent not initialized"
            });
        }

        var payload = DeserializePayload<ExplorePayload>(message.Payload);
        if (payload == null)
        {
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Invalid explore payload"
            });
        }

        _currentCts = new CancellationTokenSource();
        UpdateStatus(AgentStatuses.Thinking, "Exploring asset...");

        var workflow = new AssetExplorerWorkflow(_agentManager, _logger);
        var result = await workflow.ExploreAsync(
            payload.AssetPath, payload.Question, _currentCts.Token).ConfigureAwait(false);

        UpdateStatus(AgentStatuses.Complete, "Done");

        _currentCts?.Dispose();
        _currentCts = null;

        return CreateResponse(message.Id, "exploreResult", new
        {
            status = AgentStatuses.Complete,
            message = result
        });
    }

    private IpcMessage HandleCancel(IpcMessage message)
    {
        _currentCts?.Cancel();
        UpdateStatus(AgentStatuses.Idle, "Cancelled");

        return CreateResponse(message.Id, "cancelled", new
        {
            status = AgentStatuses.Idle,
            message = "Operation cancelled"
        })!;
    }

    private IpcMessage HandleGetStatus(IpcMessage message)
    {
        return CreateResponse(message.Id, "status", new AgentMessage(
            AgentId: "main",
            Status: _currentStatus,
            Message: _currentMessage
        ))!;
    }

    private void UpdateStatus(string status, string message)
    {
        _currentStatus = status;
        _currentMessage = message;
    }

    private static IpcMessage CreateResponse(string? id, string action, object payload)
    {
        return new IpcMessage(
            MessageTypes.Agent,
            action,
            payload,
            id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    private static T? DeserializePayload<T>(object? payload) where T : class
    {
        if (payload is JsonElement element)
        {
            return element.Deserialize<T>();
        }
        return null;
    }

    // Payload types for IPC messages
    private record ExecutePayload(string? Prompt);
    private record PortModPayload(string OriginalPath, string UpdatedPath, string ModPath, string OutputPath);
    private record ExplorePayload(string AssetPath, string Question);
}
