using System;
using System.Text.Json;

namespace UAssetViewer.Infrastructure;

/// <summary>
/// Centralized validation for untrusted payloads crossing the IPC boundary.
/// </summary>
public static class InputValidator
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryValidateRequired(
        string? value,
        string fieldName,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{fieldName} is required";
            return false;
        }

        normalizedValue = value.Trim();
        error = string.Empty;
        return true;
    }

    public static bool TryDeserializePayload<T>(
        object? payload,
        out T? result,
        out string error) where T : class
    {
        result = null;

        if (payload == null)
        {
            error = "Payload is required";
            return false;
        }

        try
        {
            result = payload switch
            {
                T typed => typed,
                JsonElement element when element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonElement element => element.Deserialize<T>(PayloadSerializerOptions),
                _ => JsonSerializer.Deserialize<T>(
                    JsonSerializer.Serialize(payload, PayloadSerializerOptions),
                    PayloadSerializerOptions),
            };
        }
        catch (JsonException ex)
        {
            error = $"Invalid payload: {ex.Message}";
            return false;
        }

        if (result == null)
        {
            error = "Invalid payload";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryGetRequiredString(
        object? payload,
        string propertyName,
        out string value,
        out string error)
    {
        value = string.Empty;

        if (!TryGetJsonElement(payload, out var element, out error))
        {
            return false;
        }

        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            error = $"{propertyName} is required";
            return false;
        }

        return TryValidateRequired(property.GetString(), propertyName, out value, out error);
    }

    private static bool TryGetJsonElement(object? payload, out JsonElement element, out string error)
    {
        if (payload is JsonElement jsonElement)
        {
            element = jsonElement;
            error = string.Empty;
            return true;
        }

        if (payload == null)
        {
            element = default;
            error = "Payload is required";
            return false;
        }

        try
        {
            element = JsonSerializer.SerializeToElement(payload, PayloadSerializerOptions);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or NotSupportedException)
        {
            element = default;
            error = $"Invalid payload: {ex.Message}";
            return false;
        }
    }
}
