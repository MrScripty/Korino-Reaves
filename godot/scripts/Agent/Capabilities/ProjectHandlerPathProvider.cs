// Project Handler Path Provider
//
// Adapter that exposes current project path from ProjectHandler.

using System;
using UAssetViewer.Bridge.Handlers;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Provides current project path from <see cref="ProjectHandler"/>.
/// </summary>
public sealed class ProjectHandlerPathProvider : IProjectPathProvider
{
    private readonly ProjectHandler _projectHandler;

    public ProjectHandlerPathProvider(ProjectHandler projectHandler)
    {
        _projectHandler = projectHandler ?? throw new ArgumentNullException(nameof(projectHandler));
    }

    /// <inheritdoc />
    public string? CurrentProjectPath => _projectHandler.CurrentProject?.Path;
}
