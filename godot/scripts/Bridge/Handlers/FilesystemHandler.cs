// Filesystem Handler - Provides filesystem operations for Svelte UI
//
// Handles fs-related IPC messages for file browser functionality.
// Allows the Svelte frontend to browse directories and select files.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public string MessageType => "fs";

    public FilesystemHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            var payload = JsonSerializer.Deserialize<ListRequest>(
                JsonSerializer.Serialize(message.Payload),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var path = payload?.Path ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

            _logger.Info("[FilesystemHandler] Listing directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                return Task.FromResult<IpcMessage?>(new IpcMessage(
                    "error",
                    "fs_error",
                    new { message = $"Directory not found: {path}" },
                    message.Id,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ));
            }

            var entries = new List<FileEntry>();

            // Add directories first
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
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
                foreach (var file in Directory.GetFiles(path))
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
                "fs",
                "listResult",
                new { entries, path = Path.GetFullPath(path) },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error listing directory");
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                "error",
                "fs_error",
                new { message = ex.Message },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
    }

    private Task<IpcMessage?> HandleGetHome(IpcMessage message)
    {
        var homePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        _logger.Info("[FilesystemHandler] Home directory: {Path}", homePath);

        var response = new IpcMessage(
            "fs",
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
            var payload = JsonSerializer.Deserialize<PathRequest>(
                JsonSerializer.Serialize(message.Payload));

            var path = payload?.Path ?? "";
            var exists = File.Exists(path) || Directory.Exists(path);
            var isDirectory = Directory.Exists(path);

            var response = new IpcMessage(
                "fs",
                "existsResult",
                new { path, exists, isDirectory },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            return Task.FromResult<IpcMessage?>(response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error checking existence");
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                "error",
                "fs_error",
                new { message = ex.Message },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
    }

    private Task<IpcMessage?> HandleGetInfo(IpcMessage message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PathRequest>(
                JsonSerializer.Serialize(message.Payload));

            var path = payload?.Path ?? "";

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var response = new IpcMessage(
                    "fs",
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
            else if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var response = new IpcMessage(
                    "fs",
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
                return Task.FromResult<IpcMessage?>(new IpcMessage(
                    "error",
                    "fs_error",
                    new { message = $"Path not found: {path}" },
                    message.Id,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FilesystemHandler] Error getting info");
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                "error",
                "fs_error",
                new { message = ex.Message },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
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
                "fs",
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
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                "error",
                "fs_error",
                new { message = ex.Message },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
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
