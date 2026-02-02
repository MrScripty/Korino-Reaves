namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// All valid IPC message categories.
/// Each category corresponds to a domain of functionality.
/// </summary>
public static class MessageTypes
{
    public const string Asset = "asset";
    public const string Tree = "tree";
    public const string Property = "property";
    public const string Selection = "selection";
    public const string Diff = "diff";
    public const string Viewport = "viewport";
    public const string Agent = "agent";
    public const string Error = "error";
    public const string Test = "test";
}

/// <summary>
/// Base IPC message structure for all communication between C# and Svelte.
/// All messages follow this format for consistent parsing and routing.
/// </summary>
/// <param name="Type">Message category for routing</param>
/// <param name="Action">Specific action within the category</param>
/// <param name="Payload">Action-specific data payload</param>
/// <param name="Id">Optional correlation ID for request/response matching</param>
/// <param name="Timestamp">Optional timestamp for message ordering</param>
public record IpcMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("payload")] object? Payload,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("timestamp")] long? Timestamp = null
);

/// <summary>
/// IPC message prefix for console.log interception.
/// </summary>
public static class IpcConstants
{
    /// <summary>
    /// Prefix used in console.log messages from frontend to backend.
    /// </summary>
    public const string IpcPrefix = "__UASSET_IPC__:";

    /// <summary>
    /// Function name called by C# to push data to Svelte.
    /// </summary>
    public const string IpcReceiver = "__UASSET_RECV__";
}
