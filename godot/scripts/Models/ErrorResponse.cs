namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Standardized error codes for IPC responses.
/// </summary>
public static class ErrorCodes
{
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string UnknownAction = "UNKNOWN_ACTION";
    public const string AssetNotLoaded = "ASSET_NOT_LOADED";
    public const string AssetLoadFailed = "ASSET_LOAD_FAILED";
    public const string AssetSaveFailed = "ASSET_SAVE_FAILED";
    public const string PropertyNotFound = "PROPERTY_NOT_FOUND";
    public const string PropertyReadOnly = "PROPERTY_READ_ONLY";
    public const string InvalidValue = "INVALID_VALUE";
    public const string DiffFailed = "DIFF_FAILED";
    public const string AgentError = "AGENT_ERROR";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InternalError = "INTERNAL_ERROR";
}

/// <summary>
/// Standardized error response structure.
/// </summary>
/// <param name="Code">Error code for programmatic handling</param>
/// <param name="Message">Human-readable error message</param>
/// <param name="Details">Additional error context</param>
/// <param name="StackTrace">Stack trace (debug builds only)</param>
public record ErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] object? Details = null,
    [property: JsonPropertyName("stackTrace")] string? StackTrace = null
);
