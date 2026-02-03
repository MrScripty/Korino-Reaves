// Asset Handler - Asset Operations via UAssetAPI
//
// Handles asset-related IPC messages using AssetManager.
// Provides open, save, close, and info operations for .uasset files.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for asset-related IPC messages.
/// Uses AssetManager for real asset operations.
/// </summary>
public sealed class AssetHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;

    public string MessageType => MessageTypes.Asset;

    public AssetHandler(IAppLogger logger, AssetManager assetManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    }

    public bool CanHandle(string action)
    {
        return action is "open" or "save" or "saveAs" or "close" or "getInfo" or "exportJson";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("AssetHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "open" => HandleOpen(message),
            "save" => HandleSave(message),
            "saveAs" => HandleSaveAs(message),
            "close" => HandleClose(message),
            "getInfo" => HandleGetInfo(message),
            "exportJson" => HandleExportJson(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private async Task<IpcMessage?> HandleOpen(IpcMessage message)
    {
        _logger.Info("Opening asset");

        try
        {
            // Parse the request
            var request = ParsePayload<OpenAssetRequest>(message.Payload);
            if (request == null || string.IsNullOrEmpty(request.FilePath))
            {
                return CreateErrorResponse(message, "Invalid open request: missing filePath");
            }

            // Load the asset
            var assetInfo = await _assetManager.LoadAsync(request.FilePath);

            return new IpcMessage(
                MessageTypes.Asset,
                "opened",
                assetInfo,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (AssetLoadException ex)
        {
            _logger.Error(ex, "Failed to open asset");
            return CreateErrorResponse(message, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error opening asset");
            return CreateErrorResponse(message, $"Failed to open asset: {ex.Message}");
        }
    }

    private async Task<IpcMessage?> HandleSave(IpcMessage message)
    {
        _logger.Info("Saving asset");

        try
        {
            if (!_assetManager.IsLoaded)
            {
                return CreateErrorResponse(message, "No asset is currently loaded");
            }

            await _assetManager.SaveAsync();

            return new IpcMessage(
                MessageTypes.Asset,
                "saved",
                new { success = true },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save asset");
            return CreateErrorResponse(message, $"Failed to save asset: {ex.Message}");
        }
    }

    private async Task<IpcMessage?> HandleSaveAs(IpcMessage message)
    {
        _logger.Info("Saving asset as new file");

        try
        {
            var path = ParsePayloadString(message.Payload, "filePath");
            if (string.IsNullOrEmpty(path))
            {
                return CreateErrorResponse(message, "Invalid saveAs request: missing filePath");
            }

            if (!_assetManager.IsLoaded)
            {
                return CreateErrorResponse(message, "No asset is currently loaded");
            }

            await _assetManager.SaveAsAsync(path);

            return new IpcMessage(
                MessageTypes.Asset,
                "saved",
                new { success = true, filePath = path },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save asset");
            return CreateErrorResponse(message, $"Failed to save asset: {ex.Message}");
        }
    }

    private Task<IpcMessage?> HandleClose(IpcMessage message)
    {
        _logger.Info("Closing asset");

        _assetManager.Close();

        var response = new IpcMessage(
            MessageTypes.Asset,
            "closed",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetInfo(IpcMessage message)
    {
        _logger.Info("Getting asset info");

        var assetInfo = _assetManager.CurrentAsset;

        var response = new IpcMessage(
            MessageTypes.Asset,
            "info",
            assetInfo,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private async Task<IpcMessage?> HandleExportJson(IpcMessage message)
    {
        _logger.Info("Exporting asset to JSON");

        try
        {
            var path = ParsePayloadString(message.Payload, "filePath");
            if (string.IsNullOrEmpty(path))
            {
                return CreateErrorResponse(message, "Invalid exportJson request: missing filePath");
            }

            if (!_assetManager.IsLoaded)
            {
                return CreateErrorResponse(message, "No asset is currently loaded");
            }

            await _assetManager.ExportJsonAsync(path);

            return new IpcMessage(
                MessageTypes.Asset,
                "exported",
                new { success = true, filePath = path },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export JSON");
            return CreateErrorResponse(message, $"Failed to export JSON: {ex.Message}");
        }
    }

    private static T? ParsePayload<T>(object? payload) where T : class
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is T typed)
        {
            return typed;
        }

        if (payload is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }

        return null;
    }

    private static string? ParsePayloadString(object? payload, string propertyName)
    {
        if (payload is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(ErrorCodes.InternalError, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }
}
