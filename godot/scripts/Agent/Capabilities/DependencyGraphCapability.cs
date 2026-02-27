// Dependency Graph Capability
//
// Enforces bounded graph traversal/query inputs and delegates to a data-access
// adapter that wraps the current metadata database implementation.

using System;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability implementation for dependency graph operations.
/// </summary>
public sealed class DependencyGraphCapability : IDependencyGraphCapability
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;
    private const int DefaultRelatedLimit = 200;
    private const int MaxRelatedLimit = 2000;
    private const int DefaultDepth = 3;
    private const int MaxDepth = 8;

    private readonly IProjectPathProvider _projectPathProvider;
    private readonly IDependencyDataAccess _dataAccess;
    private readonly IAppLogger _logger;

    public DependencyGraphCapability(
        IProjectPathProvider projectPathProvider,
        IDependencyDataAccess dataAccess,
        IAppLogger logger)
    {
        _projectPathProvider = projectPathProvider ?? throw new ArgumentNullException(nameof(projectPathProvider));
        _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public DependencyGraphStats GetStats()
    {
        var projectPath = _projectPathProvider.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return new DependencyGraphStats(false);
        }

        try
        {
            return _dataAccess.GetStats(projectPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Dependency graph stats query failed");
            return new DependencyGraphStats(false);
        }
    }

    /// <inheritdoc />
    public DependencyEdge[] GetDependencies(string assetPath, int limit = DefaultLimit)
    {
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(assetPath))
        {
            return Array.Empty<DependencyEdge>();
        }

        var boundedLimit = ClampLimit(limit, DefaultLimit, MaxLimit);
        return _dataAccess.GetDependencies(projectPath, assetPath, boundedLimit);
    }

    /// <inheritdoc />
    public DependencyEdge[] GetDependents(string assetPath, int limit = DefaultLimit)
    {
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(assetPath))
        {
            return Array.Empty<DependencyEdge>();
        }

        var boundedLimit = ClampLimit(limit, DefaultLimit, MaxLimit);
        return _dataAccess.GetDependents(projectPath, assetPath, boundedLimit);
    }

    /// <inheritdoc />
    public string[] GetRelated(string assetPath, int maxDepth = DefaultDepth, int limit = DefaultRelatedLimit)
    {
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(assetPath))
        {
            return Array.Empty<string>();
        }

        var boundedDepth = ClampLimit(maxDepth, DefaultDepth, MaxDepth);
        var boundedLimit = ClampLimit(limit, DefaultRelatedLimit, MaxRelatedLimit);
        return _dataAccess.GetRelated(projectPath, assetPath, boundedDepth, boundedLimit);
    }

    /// <inheritdoc />
    public ClassSearchHit[] SearchByClass(string className, int limit = DefaultLimit)
    {
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(className))
        {
            return Array.Empty<ClassSearchHit>();
        }

        var boundedLimit = ClampLimit(limit, DefaultLimit, MaxLimit);
        return _dataAccess.SearchByClass(projectPath, className, boundedLimit);
    }

    /// <inheritdoc />
    public PropertySearchHit[] SearchProperties(string propertyName, string? valueFilter = null, int limit = DefaultLimit)
    {
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return Array.Empty<PropertySearchHit>();
        }

        var boundedLimit = ClampLimit(limit, DefaultLimit, MaxLimit);
        return _dataAccess.SearchProperties(projectPath, propertyName, valueFilter, boundedLimit);
    }

    private string? RequireProjectPath()
    {
        return _projectPathProvider.CurrentProjectPath;
    }

    private static int ClampLimit(int requested, int fallback, int max)
    {
        if (requested <= 0)
        {
            return fallback;
        }

        return Math.Min(requested, max);
    }
}
