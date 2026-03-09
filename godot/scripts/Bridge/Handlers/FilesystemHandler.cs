// Filesystem Handler - Provides filesystem operations for Svelte UI
//
// Handles fs-related IPC messages for file browser functionality.
// Allows the Svelte frontend to browse directories and select files.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for filesystem-related IPC messages.
/// Provides directory listing and navigation for the Svelte file browser.
/// </summary>
public sealed class FilesystemHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly IReadOnlyList<string> _allowedRoots;

    public string MessageType => MessageTypes.Filesystem;

    public FilesystemHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _allowedRoots = PathValidator.GetDefaultFilesystemRoots(GetProjectsDirectoryPath());
    }

    public bool CanHandle(string action)
    {
        return action is "list" or "getHome" or "exists" or "getInfo" or "getProjectsDir";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("[FilesystemHandler] Handling action: {Action}", message.Action);

        return message.Action switch
        {
            "list" => HandleList(message),
            "getHome" => HandleGetHome(message),
            "exists" => HandleExists(message),
            "getInfo" => HandleGetInfo(message),
            "getProjectsDir" => HandleGetProjectsDir(message),
            _ => Task.FromResult<IpcMessage?>(null)
        };
    }

    private Task<IpcMessage?> HandleList(IpcMessage message)
    {
        try
        {
            var payload = message.Payload == null
                ? new ListRequest(null)
                : InputValidator.TryDeserializePayload<ListRequest>(message.Payload, out var parsedPayload, out var payloadError)
                    ? parsedPayload
                    : throw new InvalidOperationException(payloadError);

            var path = payload?.Path ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

            if (!PathValidator.TryResolveWithinRoots(
                    path,
                    _allowedRoots,
                    out var validatedPath,
                    out var pathError,
                    requireExists: true,
                    allowFiles: false,
                    allowDirectories: true))
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest));
            }

            _logger.Info("[FilesystemHandler] Listing directory: {Path}", validatedPath);

            var entries = new List<FileEntry>();

            // Add directories first
            try
            {
                foreach (var dir in Directory.GetDirectories(validatedPath))
                {
                    var info = new DirectoryInfo(dir);
                    // Skip hidden directories on Linux (starting with .)
                    if (info.Name.StartsWith('.')) continue;

                    entries.Add(new FileEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        IsDirectory = true,
                        Modified = info.LastWriteTimeUtc.ToString("o")
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }

            // Add files
            try
            {
                foreach (var file in Directory.GetFiles(validatedPath))
                {
                    var info = new FileInfo(file);
                    // Skip hidden files on Linux (starting with .)
                    if (info.Name.StartsWith('.')) continue;

                    entries.Add(new FileEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        IsDirectory = false,
                        Size = info.Length,
                        Modified = info.LastWriteTimeUtc.ToString("o")
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files we can't access
            }

            // Sort: directories first, then by name
            entries = entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var response = new IpcMessage(
                MessageTypes.Filesystem,
                "listResult",
                new { entries, path = validatedPath },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error listing directory");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleGetHome(IpcMessage message)
    {
        var homePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        _logger.Info("[FilesystemHandler] Home directory: {Path}", homePath);

        var response = new IpcMessage(
            MessageTypes.Filesystem,
            "homeResult",
            new { path = homePath },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleExists(IpcMessage message)
    {
        try
        {
            if (!InputValidator.TryDeserializePayload<PathRequest>(message.Payload, out var parsedPayload, out var payloadError))
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, payloadError, ErrorCodes.InvalidRequest));
            }

            var payload = parsedPayload!;

            if (!PathValidator.TryResolveWithinRoots(
                    payload.Path,
                    _allowedRoots,
                    out var validatedPath,
                    out var pathError,
                    requireExists: false))
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest));
            }

            var exists = File.Exists(validatedPath) || Directory.Exists(validatedPath);
            var isDirectory = Directory.Exists(validatedPath);

            var response = new IpcMessage(
                MessageTypes.Filesystem,
                "existsResult",
                new { path = validatedPath, exists, isDirectory },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error checking existence");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleGetInfo(IpcMessage message)
    {
        try
        {
            if (!InputValidator.TryDeserializePayload<PathRequest>(message.Payload, out var parsedPayload, out var payloadError))
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, payloadError, ErrorCodes.InvalidRequest));
            }

            var payload = parsedPayload!;

            if (!PathValidator.TryResolveWithinRoots(
                    payload.Path,
                    _allowedRoots,
                    out var validatedPath,
                    out var pathError,
                    requireExists: true))
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest));
            }

            if (Directory.Exists(validatedPath))
            {
                var info = new DirectoryInfo(validatedPath);
                var response = new IpcMessage(
                    MessageTypes.Filesystem,
                    "infoResult",
                    new FileEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        IsDirectory = true,
                        Modified = info.LastWriteTimeUtc.ToString("o")
                    },
                    message.Id,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );
                return Task.FromResult<IpcMessage?>(response);
            }
            else if (File.Exists(validatedPath))
            {
                var info = new FileInfo(validatedPath);
                var response = new IpcMessage(
                    MessageTypes.Filesystem,
                    "infoResult",
                    new FileEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        IsDirectory = false,
                        Size = info.Length,
                        Modified = info.LastWriteTimeUtc.ToString("o")
                    },
                    message.Id,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );
                return Task.FromResult<IpcMessage?>(response);
            }
            else
            {
                return Task.FromResult<IpcMessage?>(CreateErrorResponse(
                    message,
                    $"Path not found: {validatedPath}",
                    ErrorCodes.InvalidRequest));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error getting info");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleGetProjectsDir(IpcMessage message)
    {
        try
        {
            // Get the projects directory path (same logic as ProjectHandler)
            var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
            projectRoot = Path.GetDirectoryName(projectRoot) ?? projectRoot;
            var projectsDir = Path.Combine(projectRoot, "projects");

            // Ensure the directory exists
            if (!Directory.Exists(projectsDir))
            {
                Directory.CreateDirectory(projectsDir);
            }

            _logger.Info("[FilesystemHandler] Projects directory: {Path}", projectsDir);

            var response = new IpcMessage(
                MessageTypes.Filesystem,
                "projectsDirResult",
                new { path = projectsDir },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error getting projects directory");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private static string GetProjectsDirectoryPath()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
        projectRoot = Path.GetDirectoryName(projectRoot) ?? projectRoot;
        return Path.Combine(projectRoot, "projects");
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage, string code = ErrorCodes.InternalError)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "fs_error",
            new ErrorResponse(code, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    // Request DTOs
    private record ListRequest(string? Path);
    private record PathRequest(string? Path);

    // Response DTO
    private record FileEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public required string Name { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public required string Path { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("isDirectory")]
        public required bool IsDirectory { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("size")]
        public long? Size { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("modified")]
        public string? Modified { get; init; }
    }
}
