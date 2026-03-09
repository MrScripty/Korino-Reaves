using System;
using System.Text.Json;
using UAssetViewer.Models;

namespace UAssetViewer.Infrastructure;

/// <summary>
/// Validates incoming IPC envelopes before they are dispatched.
/// </summary>
public static class IpcMessageValidator
{
    public static bool TryParseIncomingMessage(
        string json,
        out IpcMessage? message,
        out string error)
    {
        message = null;

        if (!InputValidator.TryValidateRequired(json, "ipc message", out var normalizedJson, out error))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "IPC message must be a JSON object";
                return false;
            }

            if (!TryGetRequiredString(root, "type", out var type, out error))
            {
                return false;
            }

            if (!MessageTypes.IsKnown(type))
            {
                error = $"Unknown IPC message type: {type}";
                return false;
            }

            if (!TryGetRequiredString(root, "action", out var action, out error))
            {
                return false;
            }

            object? payload = null;
            if (root.TryGetProperty("payload", out var payloadElement))
            {
                payload = payloadElement.Clone();
            }

            string? id = null;
            if (root.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    id = null;
                }
                else if (idElement.ValueKind == JsonValueKind.String)
                {
                    id = idElement.GetString();
                }
                else
                {
                    error = "IPC message id must be a string when provided";
                    return false;
                }
            }

            long? timestamp = null;
            if (root.TryGetProperty("timestamp", out var timestampElement))
            {
                if (timestampElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    timestamp = null;
                }
                else if (timestampElement.ValueKind == JsonValueKind.Number &&
                         timestampElement.TryGetInt64(out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
                else
                {
                    error = "IPC message timestamp must be a number when provided";
                    return false;
                }
            }

            message = new IpcMessage(type, action, payload, id, timestamp);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid IPC JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string value,
        out string error)
    {
        value = string.Empty;

        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            error = $"IPC message {propertyName} must be a string";
            return false;
        }

        return InputValidator.TryValidateRequired(property.GetString(), propertyName, out value, out error);
    }
}
