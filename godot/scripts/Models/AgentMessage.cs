namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Current status of an AI agent operation.
/// </summary>
public static class AgentStatuses
{
    public const string Idle = "idle";
    public const string Thinking = "thinking";
    public const string Executing = "executing";
    public const string Waiting = "waiting";
    public const string Complete = "complete";
    public const string Error = "error";
}

/// <summary>
/// Message from an AI agent about its current state.
/// </summary>
/// <param name="AgentId">Unique identifier for this agent instance</param>
/// <param name="Status">Current operation status</param>
/// <param name="Message">Human-readable status message</param>
/// <param name="Progress">Progress percentage (0-100), if determinable</param>
/// <param name="CurrentAction">Current action being performed</param>
/// <param name="PendingActions">Pending actions in queue</param>
public record AgentMessage(
    [property: JsonPropertyName("agentId")] string AgentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("progress")] int? Progress = null,
    [property: JsonPropertyName("currentAction")] string? CurrentAction = null,
    [property: JsonPropertyName("pendingActions")] string[]? PendingActions = null
);

/// <summary>
/// Command to send to an AI agent.
/// </summary>
/// <param name="AgentId">Target agent ID</param>
/// <param name="Command">Command type</param>
/// <param name="Params">Command parameters</param>
public record AgentCommand(
    [property: JsonPropertyName("agentId")] string AgentId,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("params")] Dictionary<string, object?>? Params = null
);

/// <summary>
/// Valid agent command types.
/// </summary>
public static class AgentCommands
{
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Query = "query";
}
