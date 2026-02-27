// Dependency Data Access
//
// Data-access abstraction used by dependency and metadata capabilities.
// Implementations can wrap SQLite, mocks, or in-memory stores.

using System.Threading;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Provides dependency graph and metadata queries for an open project.
/// </summary>
public interface IDependencyDataAccess
{
    /// <summary>
    /// Gets graph stats for a project.
    /// </summary>
    DependencyGraphStats GetStats(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Gets direct dependencies for an asset.
    /// </summary>
    DependencyEdge[] GetDependencies(string projectPath, string assetPath, int limit, CancellationToken ct = default);

    /// <summary>
    /// Gets direct dependents for an asset.
    /// </summary>
    DependencyEdge[] GetDependents(string projectPath, string assetPath, int limit, CancellationToken ct = default);

    /// <summary>
    /// Gets a bounded related cluster from an asset.
    /// </summary>
    string[] GetRelated(string projectPath, string assetPath, int maxDepth, int limit, CancellationToken ct = default);

    /// <summary>
    /// Searches assets by class name.
    /// </summary>
    ClassSearchHit[] SearchByClass(string projectPath, string className, int limit, CancellationToken ct = default);

    /// <summary>
    /// Searches property values by property name and optional value filter.
    /// </summary>
    PropertySearchHit[] SearchProperties(string projectPath, string propertyName, string? valueFilter, int limit, CancellationToken ct = default);

    /// <summary>
    /// Gets a bounded metadata snapshot for a single asset.
    /// </summary>
    AssetMetadataSnapshot? GetAssetMetadata(string projectPath, string assetPath, int rowLimit, CancellationToken ct = default);
}
