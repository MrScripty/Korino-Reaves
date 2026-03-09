// Asset Handler - Asset Operations via UAssetAPI
//
// Handles asset-related IPC messages using AssetManager.
// Provides open, save, close, and info operations for .uasset files.

using System;
using System.Collections.Generic;
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
    private readonly IReadOnlyList<string> _allowedRoots;

    public string MessageType => MessageTypes.Asset;

    public AssetHandler(IAppLogger logger, AssetManager assetManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
        _allowedRoots = PathValidator.GetDefaultFilesystemRoots();
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
            if (!InputValidator.TryDeserializePayload<OpenAssetRequest>(message.Payload, out var parsedRequest, out var payloadError))
            {
                return CreateErrorResponse(message, payloadError, ErrorCodes.InvalidRequest);
            }

            var request = parsedRequest!;

            if (!PathValidator.TryResolveWithinRoots(
                    request.FilePath,
                    _allowedRoots,
                    out var validatedPath,
                    out var pathError,
                    requireExists: true,
                    allowFiles: true,
                    allowDirectories: false))
            {
                return CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest);
            }

            var assetInfo = await _assetManager.LoadAsync(validatedPath);

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
            if (!InputValidator.TryGetRequiredString(message.Payload, "filePath", out var path, out var payloadError))
            {
                return CreateErrorResponse(message, payloadError, ErrorCodes.InvalidRequest);
            }

            if (!_assetManager.IsLoaded)
            {
                return CreateErrorResponse(message, "No asset is currently loaded");
            }

            if (!PathValidator.TryResolveWithinRoots(
                    path,
                    _allowedRoots,
                    out var validatedPath,
                    out var pathError,
                    requireExists: false,
                    allowFiles: true,
                    allowDirectories: false))
            {
                return CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest);
            }

            await _assetManager.SaveAsAsync(validatedPath);

            return new IpcMessage(
                MessageTypes.Asset,
                "saved",
                new { success = true, filePath = validatedPath },
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
            if (!InputValidator.TryGetRequiredString(message.Payload, "filePath", out var path, out var payloadError))
            {
                return CreateErrorResponse(message, payloadError, ErrorCodes.InvalidRequest);
            }

            if (!_assetManager.IsLoaded)
            {
                return CreateErrorResponse(message, "No asset is currently loaded");
            }

            if (!PathValidator.TryResolveWithinRoots(
                    path,
                    _allowedRoots,
                    out var validatedPath,
                    out var pathError,
                    requireExists: false,
                    allowFiles: true,
                    allowDirectories: false))
            {
                return CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest);
            }

            await _assetManager.ExportJsonAsync(validatedPath);

            return new IpcMessage(
                MessageTypes.Asset,
                "exported",
                new { success = true, filePath = validatedPath },
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

    private static IpcMessage CreateErrorResponse(
        IpcMessage request,
        string errorMessage,
        string code = ErrorCodes.InternalError)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(code, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }
}
