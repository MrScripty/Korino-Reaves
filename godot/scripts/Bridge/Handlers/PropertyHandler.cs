// Property Handler - Property Operations via AssetManager
//
// Handles property-related IPC messages using AssetManager.
// Provides property reading, editing, and edit tracking via SQLite.

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetViewer.Assets;
using UAssetViewer.Bridge;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for property editing IPC messages.
/// Uses AssetManager for real property operations and
/// EditDatabase for persistent edit tracking.
/// </summary>
public sealed class PropertyHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;
    private readonly EditDatabase _editDatabase;
    private readonly IpcDispatcher _dispatcher;
    private string? _currentFilePath;

    public string MessageType => MessageTypes.Property;

    public PropertyHandler(IAppLogger logger, AssetManager assetManager, EditDatabase editDatabase, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
        _editDatabase = editDatabase ?? throw new ArgumentNullException(nameof(editDatabase));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CanHandle(string action)
    {
        return action is "get" or "set" or "getForNode" or "reset" or "getEditedFiles";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("PropertyHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "get" => HandleGet(message),
            "set" => HandleSet(message),
            "getForNode" => HandleGetForNode(message),
            "reset" => HandleReset(message),
            "getEditedFiles" => HandleGetEditedFiles(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    /// <summary>
    /// Sets the relative file path of the currently loaded asset.
    /// Called by MainController when an asset file is loaded.
    /// </summary>
    public void SetCurrentFilePath(string? relativePath)
    {
        _currentFilePath = relativePath;
    }

    /// <summary>
    /// Reapplies saved edits from the database to the in-memory asset.
    /// Called after loading an asset to restore previous edits.
    /// </summary>
    public void ReapplyEdits()
    {
        if (!_editDatabase.IsOpen || _currentFilePath == null || !_assetManager.IsLoaded)
            return;

        var edits = _editDatabase.GetEditsForFile(_currentFilePath);
        foreach (var edit in edits)
        {
            try
            {
                var path = JsonSerializer.Deserialize<string[]>(edit.PropertyPath);
                if (path == null || path.Length == 0) continue;

                var value = DeserializeValueForApply(edit.EditedValue, edit.PropertyType);
                if (value != null)
                {
                    _assetManager.SetPropertyValue(path, value);
                    _logger.Info("Reapplied edit: {Path}", edit.PropertyPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to reapply edit: {Path} - {Error}",
                    edit.PropertyPath, ex.Message);
            }
        }
    }

    /// <summary>
    /// Pushes properties for the given node ID to the frontend.
    /// Called by the selection change subscription for auto-push.
    /// Annotates properties with edit status from the database.
    /// </summary>
    public void PushPropertiesForNode(string nodeId, IpcDispatcher dispatcher)
    {
        var exportId = ResolveExportId(nodeId);
        if (exportId == null || !_assetManager.IsLoaded)
        {
            // Non-export node or no asset loaded — send empty so the frontend clears loading state
            dispatcher.Send(MessageTypes.Property, "update",
                new { path = nodeId, properties = Array.Empty<PropertyValue>() });
            return;
        }

        try
        {
            var properties = _assetManager.GetProperties(exportId);
            properties = AnnotateEditStatus(properties);
            dispatcher.Send(MessageTypes.Property, "update", new { path = nodeId, properties });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to push properties for node: {NodeId}", nodeId);
            dispatcher.Send(MessageTypes.Property, "error", new { message = $"Failed to load properties: {ex.Message}" });
        }
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

            // Capture original value before mutation (only on first edit)
            if (_editDatabase.IsOpen && _currentFilePath != null)
            {
                RecordEdit(path, value);
            }

            _assetManager.SetPropertyValue(path, value!);

            // Push updated edited-files list to frontend
            PushEditedFiles();

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
            _logger.Warning("Invalid property value: {Path} = {Value}", string.Join("/", ex.Path), ex.Value!);
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set property value");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, $"Failed to set property: {ex.Message}"));
        }
    }

    private Task<IpcMessage?> HandleReset(IpcMessage message)
    {
        _logger.Info("Resetting property to original value");

        if (!_assetManager.IsLoaded || _currentFilePath == null || !_editDatabase.IsOpen)
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Cannot reset: no asset or database"));
        }

        try
        {
            var path = ParsePath(message.Payload);
            if (path == null || path.Length == 0)
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path in request"));
            }

            var propertyPathJson = JsonSerializer.Serialize(path);
            var edit = _editDatabase.GetEdit(_currentFilePath, propertyPathJson);
            if (edit == null)
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No edit record found"));
            }

            // Restore original value
            var originalValue = DeserializeValueForApply(edit.OriginalValue, edit.PropertyType);
            if (originalValue != null)
            {
                _assetManager.SetPropertyValue(path, originalValue);
            }

            // Delete the edit record
            _editDatabase.DeleteEdit(_currentFilePath, propertyPathJson);

            // Re-push properties with updated isEdited flags
            var exportId = ResolveExportId(path[0]);
            if (exportId != null)
            {
                PushPropertiesForNode(exportId, _dispatcher);
            }

            // Push updated edited-files list
            PushEditedFiles();

            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Property, "reset",
                new { success = true, path },
                message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset property");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, $"Failed to reset: {ex.Message}"));
        }
    }

    private Task<IpcMessage?> HandleGetEditedFiles(IpcMessage message)
    {
        var editedFiles = _editDatabase.IsOpen
            ? _editDatabase.GetEditedFilePaths().ToArray()
            : Array.Empty<string>();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Property, "editedFiles",
            new { files = editedFiles },
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
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
        properties = AnnotateEditStatus(properties);

        var response = new IpcMessage(
            MessageTypes.Property,
            "update",
            new { path = nodeId, properties },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    // =========================================================================
    // Edit Tracking Helpers
    // =========================================================================

    private void RecordEdit(string[] path, object? newValue)
    {
        try
        {
            var propertyPathJson = JsonSerializer.Serialize(path);
            var existingEdit = _editDatabase.GetEdit(_currentFilePath!, propertyPathJson);

            // Get original value only on first edit
            string? originalJson;
            if (existingEdit != null)
            {
                originalJson = existingEdit.OriginalValue;
            }
            else
            {
                try
                {
                    var original = _assetManager.GetPropertyValue(path);
                    originalJson = JsonSerializer.Serialize(original);
                }
                catch
                {
                    originalJson = null;
                }
            }

            // Determine property type
            var propertyType = "unknown";
            try
            {
                var exportId = ResolveExportId(path[0]);
                if (exportId != null)
                {
                    var props = _assetManager.GetProperties(exportId);
                    var match = props.FirstOrDefault(p => PathsEqual(p.Path, path));
                    if (match != null) propertyType = match.Type;
                }
            }
            catch { /* ignore */ }

            var now = DateTime.UtcNow;
            _editDatabase.SaveEdit(new PropertyEdit(
                FilePath: _currentFilePath!,
                PropertyPath: propertyPathJson,
                OriginalValue: originalJson,
                EditedValue: JsonSerializer.Serialize(newValue),
                PropertyType: propertyType,
                CreatedAt: existingEdit?.CreatedAt ?? now,
                UpdatedAt: now
            ));
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to record edit: {Error}", ex.Message);
        }
    }

    private PropertyValue[] AnnotateEditStatus(PropertyValue[] properties)
    {
        if (!_editDatabase.IsOpen || _currentFilePath == null) return properties;

        var edits = _editDatabase.GetEditsForFile(_currentFilePath);
        if (edits.Count == 0) return properties;

        var editedPaths = new System.Collections.Generic.HashSet<string>(
            edits.Select(e => e.PropertyPath));

        return properties.Select(p =>
        {
            var pathJson = JsonSerializer.Serialize(p.Path);
            return editedPaths.Contains(pathJson) ? p with { IsEdited = true } : p;
        }).ToArray();
    }

    /// <summary>
    /// Pushes the list of edited file paths to the frontend.
    /// </summary>
    public void PushEditedFiles()
    {
        if (!_editDatabase.IsOpen) return;
        var editedFiles = _editDatabase.GetEditedFilePaths().ToArray();
        _dispatcher.Send(MessageTypes.Property, "editedFiles", new { files = editedFiles });
    }

    private static object? DeserializeValueForApply(string? json, string propertyType)
    {
        if (json == null) return null;

        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when propertyType == "number" && element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static bool PathsEqual(string[] a, string[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    // =========================================================================
    // Parsing Helpers
    // =========================================================================

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
            "update",
            new { path = (string?)null, properties = Array.Empty<PropertyValue>() },
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

    /// <summary>
    /// Extracts the root export ID from a node ID.
    /// Handles both "export-0" and "export-0/property-2-Name" formats.
    /// </summary>
    private static string? ResolveExportId(string nodeId)
    {
        if (!nodeId.StartsWith("export-"))
            return null;

        var slashIndex = nodeId.IndexOf('/');
        return slashIndex < 0 ? nodeId : nodeId.Substring(0, slashIndex);
    }
}
