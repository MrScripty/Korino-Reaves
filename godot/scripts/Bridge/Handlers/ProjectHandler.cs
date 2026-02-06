// Project Handler - Project Operations
//
// Handles project-related IPC messages for opening and managing
// extracted PAK contents as projects.

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
/// Information about a project (extracted PAK contents).
/// </summary>
public sealed record ProjectInfo(
    string Name,
    string Path,
    int FileCount,
    string? LastModified = null
);

/// <summary>
/// Request to open a project.
/// </summary>
public sealed record OpenProjectRequest(
    string ProjectPath
);

/// <summary>
/// Handler for project-related IPC messages.
/// Manages opening and listing extracted PAK projects.
/// </summary>
public sealed class ProjectHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private ProjectInfo? _currentProject;

    /// <summary>
    /// Gets the currently open project, or null if no project is open.
    /// </summary>
    public ProjectInfo? CurrentProject => _currentProject;

    public string MessageType => MessageTypes.Project;

    public ProjectHandler(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CanHandle(string action)
    {
        return action is "open" or "list" or "close" or "getTree";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("ProjectHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "open" => HandleOpen(message),
            "list" => HandleList(message),
            "close" => HandleClose(message),
            "getTree" => HandleGetTree(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private async Task<IpcMessage?> HandleOpen(IpcMessage message)
    {
        try
        {
            var request = ParsePayload<OpenProjectRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                return CreateErrorResponse(message, "Invalid project path");
            }

            var projectPath = request.ProjectPath;

            // Validate directory exists
            if (!Directory.Exists(projectPath))
            {
                return CreateErrorResponse(message, $"Project directory not found: {projectPath}");
            }

            _logger.Info("Opening project: {Path}", projectPath);

            // Get project name from directory
            var projectName = System.IO.Path.GetFileName(projectPath);

            // Count files
            var fileCount = await Task.Run(() => CountFiles(projectPath));

            // Build project info
            _currentProject = new ProjectInfo(
                projectName,
                projectPath,
                fileCount,
                Directory.GetLastWriteTime(projectPath).ToString("o")
            );

            // Build and send the file tree
            var treeNodes = await Task.Run(() => BuildFileTree(projectPath));

            // Send tree update
            _dispatcher.Send(MessageTypes.Tree, "update", new { nodes = treeNodes });

            // Return project opened response
            return new IpcMessage(
                MessageTypes.Project,
                "opened",
                _currentProject,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open project");
            return CreateErrorResponse(message, ex.Message);
        }
    }

    private Task<IpcMessage?> HandleList(IpcMessage message)
    {
        try
        {
            // Get projects directory
            var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot) ?? projectRoot;
            var projectsDir = System.IO.Path.Combine(projectRoot, "projects");

            var projects = new List<ProjectInfo>();

            if (Directory.Exists(projectsDir))
            {
                foreach (var dir in Directory.GetDirectories(projectsDir))
                {
                    var name = System.IO.Path.GetFileName(dir);
                    var ueDataPath = System.IO.Path.Combine(dir, "UE_data");

                    if (Directory.Exists(ueDataPath))
                    {
                        var fileCount = CountFiles(ueDataPath);
                        var lastModified = Directory.GetLastWriteTime(dir).ToString("o");
                        projects.Add(new ProjectInfo(name, ueDataPath, fileCount, lastModified));
                    }
                }
            }

            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Project,
                "list",
                new { projects },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to list projects");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleClose(IpcMessage message)
    {
        _currentProject = null;

        // Clear the tree
        _dispatcher.Send(MessageTypes.Tree, "update", new { nodes = Array.Empty<TreeNode>() });

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Project,
            "closed",
            null,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetTree(IpcMessage message)
    {
        if (_currentProject == null)
        {
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Tree,
                "update",
                new { nodes = Array.Empty<TreeNode>() },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }

        var treeNodes = BuildFileTree(_currentProject.Path);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Tree,
            "update",
            new { nodes = treeNodes },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    /// <summary>
    /// Builds a file tree from a directory.
    /// </summary>
    private TreeNode[] BuildFileTree(string rootPath)
    {
        var rootNodes = new List<TreeNode>();

        try
        {
            // Get top-level directories
            foreach (var dir in Directory.GetDirectories(rootPath).OrderBy(d => d))
            {
                var node = BuildDirectoryNode(dir, rootPath);
                rootNodes.Add(node);
            }

            // Get top-level files
            foreach (var file in Directory.GetFiles(rootPath).OrderBy(f => f))
            {
                var node = BuildFileNode(file, rootPath);
                rootNodes.Add(node);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error building file tree for: {Path}", rootPath);
        }

        return rootNodes.ToArray();
    }

    private TreeNode BuildDirectoryNode(string dirPath, string rootPath)
    {
        var relativePath = System.IO.Path.GetRelativePath(rootPath, dirPath);
        var name = System.IO.Path.GetFileName(dirPath);
        var id = $"folder:{relativePath.Replace(System.IO.Path.DirectorySeparatorChar, '/')}";

        // Check if has children
        var hasChildren = Directory.EnumerateFileSystemEntries(dirPath).Any();

        // Build children (lazy - only immediate children)
        var children = new List<TreeNode>();

        foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(d => d))
        {
            children.Add(BuildDirectoryNode(subDir, rootPath));
        }

        foreach (var file in Directory.GetFiles(dirPath).OrderBy(f => f))
        {
            children.Add(BuildFileNode(file, rootPath));
        }

        return new TreeNode(
            id,
            name,
            TreeNodeTypes.Folder,
            hasChildren,
            children.Count > 0 ? children.ToArray() : null,
            null
        );
    }

    private TreeNode BuildFileNode(string filePath, string rootPath)
    {
        var relativePath = System.IO.Path.GetRelativePath(rootPath, filePath);
        var name = System.IO.Path.GetFileName(filePath);
        var id = $"file:{relativePath.Replace(System.IO.Path.DirectorySeparatorChar, '/')}";

        // Get file extension for metadata
        var extension = System.IO.Path.GetExtension(filePath).TrimStart('.');

        return new TreeNode(
            id,
            name,
            TreeNodeTypes.File,
            false, // Files don't have children
            null,
            new TreeNodeMetadata(null, extension, null, null, null)
        );
    }

    private static int CountFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static T? ParsePayload<T>(object? payload) where T : class
    {
        if (payload == null) return null;
        if (payload is T typed) return typed;
        if (payload is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
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
