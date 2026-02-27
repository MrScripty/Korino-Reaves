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
    private readonly Action<IpcMessage>? _emit;
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _currentCts;
    private string _currentStatus = AgentStatuses.Idle;
    private string _currentMessage = "";

    public AgentHandler(
        IAppLogger logger,
        AgentManager? agentManager = null,
        Action<IpcMessage>? emit = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentManager = agentManager;
        _emit = emit;
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
            var unavailable = "Agent not initialized - Ollama may not be running";
            EmitError(message.Id, unavailable);
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = unavailable
            });
        }

        var payload = DeserializePayload<ExecutePayload>(message.Payload);
        if (payload?.Prompt == null)
        {
            EmitError(message.Id, "Missing 'prompt' in payload");
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Missing 'prompt' in payload"
            });
        }

        _currentCts = new CancellationTokenSource();
        UpdateStatus(message.Id, AgentStatuses.Thinking, "Processing prompt...");
        EmitStep(message.Id, "execute", "Invoking model and tool calls");

        try
        {
            var result = await _agentManager.ExecuteAsync(
                payload.Prompt, _currentCts.Token).ConfigureAwait(false);

            UpdateStatus(message.Id, AgentStatuses.Complete, "Done");

            return CreateResponse(message.Id, "result", new
            {
                status = AgentStatuses.Complete,
                message = result
            });
        }
        catch (OperationCanceledException)
        {
            UpdateStatus(message.Id, AgentStatuses.Idle, "Cancelled");
            return CreateResponse(message.Id, "result", new
            {
                status = AgentStatuses.Idle,
                message = "Execution cancelled"
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Agent execute failed");
            UpdateStatus(message.Id, AgentStatuses.Error, ex.Message);
            EmitError(message.Id, ex.Message);
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
            EmitError(message.Id, "Agent not initialized");
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Agent not initialized"
            });
        }

        var payload = DeserializePayload<PortModPayload>(message.Payload);
        if (payload == null)
        {
            EmitError(message.Id, "Invalid portMod payload");
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Invalid portMod payload"
            });
        }

        _currentCts = new CancellationTokenSource();
        UpdateStatus(message.Id, AgentStatuses.Executing, "Porting mod...");
        EmitStep(message.Id, "portMod", "Running mod porting workflow");

        var workflow = new ModPortingWorkflow(_agentManager, _logger);
        var result = await workflow.ExecuteAsync(
            payload.OriginalPath,
            payload.UpdatedPath,
            payload.ModPath,
            payload.OutputPath,
            _currentCts.Token).ConfigureAwait(false);

        var resultStatus = result.Success ? AgentStatuses.Complete : AgentStatuses.Error;
        UpdateStatus(message.Id, resultStatus, result.Message);
        if (!result.Success)
        {
            EmitError(message.Id, result.Error ?? result.Message);
        }

        _currentCts?.Dispose();
        _currentCts = null;

        return CreateResponse(message.Id, "result", new
        {
            status = resultStatus,
            message = result.Message,
            data = result
        });
    }

    private async Task<IpcMessage?> HandleExplore(IpcMessage message)
    {
        if (_agentManager == null)
        {
            EmitError(message.Id, "Agent not initialized");
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Agent not initialized"
            });
        }

        var payload = DeserializePayload<ExplorePayload>(message.Payload);
        if (payload == null)
        {
            EmitError(message.Id, "Invalid explore payload");
            return CreateResponse(message.Id, "error", new
            {
                status = AgentStatuses.Error,
                message = "Invalid explore payload"
            });
        }

        _currentCts = new CancellationTokenSource();
        UpdateStatus(message.Id, AgentStatuses.Thinking, "Exploring asset...");
        EmitStep(message.Id, "explore", "Running asset explorer workflow");

        var workflow = new AssetExplorerWorkflow(_agentManager, _logger);
        var result = await workflow.ExploreAsync(
            payload.AssetPath, payload.Question, _currentCts.Token).ConfigureAwait(false);

        UpdateStatus(message.Id, AgentStatuses.Complete, "Done");

        _currentCts?.Dispose();
        _currentCts = null;

        return CreateResponse(message.Id, "result", new
        {
            status = AgentStatuses.Complete,
            message = result
        });
    }

    private IpcMessage HandleCancel(IpcMessage message)
    {
        _currentCts?.Cancel();
        UpdateStatus(message.Id, AgentStatuses.Idle, "Cancelled");

        return CreateResponse(message.Id, "result", new
        {
            status = AgentStatuses.Idle,
            message = "Operation cancelled"
        })!;
    }

    private IpcMessage HandleGetStatus(IpcMessage message)
    {
        return CreateResponse(message.Id, "status", CreateAgentMessage(
            _currentStatus,
            _currentMessage));
    }

    private void UpdateStatus(string? requestId, string status, string message)
    {
        _currentStatus = status;
        _currentMessage = message;
        EmitStatus(requestId, status, message);
    }

    private void EmitStatus(string? requestId, string status, string message)
    {
        Emit("status", CreateAgentMessage(status, message), requestId);
    }

    private void EmitStep(string? requestId, string step, string message)
    {
        Emit("step", new
        {
            step,
            message,
            status = _currentStatus
        }, requestId);
    }

    private void EmitError(string? requestId, string message)
    {
        Emit("error", new
        {
            status = AgentStatuses.Error,
            message
        }, requestId);
    }

    private AgentMessage CreateAgentMessage(string status, string message)
    {
        return new AgentMessage(
            AgentId: "main",
            Status: status,
            Message: message
        );
    }

    private void Emit(string action, object payload, string? requestId = null)
    {
        _emit?.Invoke(new IpcMessage(
            MessageTypes.Agent,
            action,
            payload,
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
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
