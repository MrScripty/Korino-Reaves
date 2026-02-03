// Property Handler - Property Operations via AssetManager
//
// Handles property-related IPC messages using AssetManager.
// Provides property reading and editing for asset exports.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for property editing IPC messages.
/// Uses AssetManager for real property operations.
/// </summary>
public sealed class PropertyHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;

    public string MessageType => MessageTypes.Property;

    public PropertyHandler(IAppLogger logger, AssetManager assetManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    }

    public bool CanHandle(string action)
    {
        return action is "get" or "set" or "getForNode";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("PropertyHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "get" => HandleGet(message),
            "set" => HandleSet(message),
            "getForNode" => HandleGetForNode(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandleGet(IpcMessage message)
    {
        _logger.Info("Getting property value");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset is currently loaded"));
        }

        try
        {
            var path = ParsePath(message.Payload);
            if (path == null || path.Length == 0)
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path in request"));
            }

            var value = _assetManager.GetPropertyValue(path);

            var response = new IpcMessage(
                MessageTypes.Property,
                "value",
                new { path, value },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (PropertyNotFoundException ex)
        {
            _logger.Warning("Property not found: {Path}", string.Join("/", ex.Path));
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get property value");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, $"Failed to get property: {ex.Message}"));
        }
    }

    private Task<IpcMessage?> HandleSet(IpcMessage message)
    {
        _logger.Info("Setting property value");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset is currently loaded"));
        }

        try
        {
            var (path, value) = ParseSetRequest(message.Payload);
            if (path == null || path.Length == 0)
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path in request"));
            }

            _assetManager.SetPropertyValue(path, value!);

            var response = new IpcMessage(
                MessageTypes.Property,
                "updated",
                new { success = true, path },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (PropertyNotFoundException ex)
        {
            _logger.Warning("Property not found: {Path}", string.Join("/", ex.Path));
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
        catch (InvalidPropertyValueException ex)
        {
            _logger.Warning("Invalid property value: {Path} = {Value}", string.Join("/", ex.Path), ex.Value);
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set property value");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, $"Failed to set property: {ex.Message}"));
        }
    }

    private Task<IpcMessage?> HandleGetForNode(IpcMessage message)
    {
        _logger.Info("Getting properties for node");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateEmptyResponse(message));
        }

        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing nodeId in request"));
        }

        var properties = _assetManager.GetProperties(nodeId);

        var response = new IpcMessage(
            MessageTypes.Property,
            "properties",
            new { nodeId, properties },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private static string[]? ParsePath(object? payload)
    {
        if (payload is JsonElement element && element.TryGetProperty("path", out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Array)
            {
                var length = prop.GetArrayLength();
                var path = new string[length];
                int i = 0;
                foreach (var item in prop.EnumerateArray())
                {
                    path[i++] = item.GetString() ?? "";
                }
                return path;
            }
        }
        return null;
    }

    private static (string[]? path, object? value) ParseSetRequest(object? payload)
    {
        if (payload is JsonElement element)
        {
            string[]? path = null;
            object? value = null;

            if (element.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.Array)
            {
                var length = pathProp.GetArrayLength();
                path = new string[length];
                int i = 0;
                foreach (var item in pathProp.EnumerateArray())
                {
                    path[i++] = item.GetString() ?? "";
                }
            }

            if (element.TryGetProperty("value", out var valueProp))
            {
                value = valueProp.ValueKind switch
                {
                    JsonValueKind.String => valueProp.GetString(),
                    JsonValueKind.Number when valueProp.TryGetInt32(out var i) => i,
                    JsonValueKind.Number when valueProp.TryGetInt64(out var l) => l,
                    JsonValueKind.Number => valueProp.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }

            return (path, value);
        }
        return (null, null);
    }

    private static string? ParseNodeId(object? payload)
    {
        if (payload is JsonElement element)
        {
            if (element.TryGetProperty("nodeId", out var prop))
            {
                return prop.GetString();
            }
            if (element.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString();
            }
        }
        return null;
    }

    private static IpcMessage CreateEmptyResponse(IpcMessage request)
    {
        return new IpcMessage(
            MessageTypes.Property,
            "properties",
            new { nodeId = (string?)null, properties = Array.Empty<PropertyValue>() },
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(ErrorCodes.InvalidRequest, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }
}
