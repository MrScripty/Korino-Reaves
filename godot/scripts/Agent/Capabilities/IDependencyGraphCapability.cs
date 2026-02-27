// Dependency Graph Capability
//
// Capability contract for dependency graph traversal and search.

using System.Threading;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability for dependency graph operations.
/// </summary>
public interface IDependencyGraphCapability
{
    /// <summary>
    /// Gets dependency graph stats for the current project.
    /// </summary>
    DependencyGraphStats GetStats(CancellationToken ct = default);

    /// <summary>
    /// Gets direct dependencies for an asset path.
    /// </summary>
    DependencyEdge[] GetDependencies(string assetPath, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Gets direct dependents for an asset path.
    /// </summary>
    DependencyEdge[] GetDependents(string assetPath, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Gets a related cluster around an asset path.
    /// </summary>
    string[] GetRelated(string assetPath, int maxDepth = 3, int limit = 200, CancellationToken ct = default);

    /// <summary>
    /// Searches assets by class name.
    /// </summary>
    ClassSearchHit[] SearchByClass(string className, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Searches properties by name and optional value filter.
    /// </summary>
    PropertySearchHit[] SearchProperties(string propertyName, string? valueFilter = null, int limit = 100, CancellationToken ct = default);
}
