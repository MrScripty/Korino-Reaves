// Dependency Graph Plugin - Semantic Kernel functions for dependency traversal
//
// Exposes dependency graph stats, neighborhood traversal, and search
// through the capability layer.

using System.ComponentModel;
using Microsoft.SemanticKernel;
using UAssetViewer.Agent.Capabilities;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for dependency graph operations.
/// </summary>
public sealed class DependencyGraphPlugin
{
    private readonly IDependencyGraphCapability _dependencyGraph;

    public DependencyGraphPlugin(IDependencyGraphCapability dependencyGraph)
    {
        _dependencyGraph = dependencyGraph;
    }

    [KernelFunction("get_dependency_graph_stats")]
    [Description("Gets dependency graph availability and summary statistics for the current project.")]
    public DependencyGraphStats GetDependencyGraphStats()
    {
        return _dependencyGraph.GetStats();
    }

    [KernelFunction("get_dependencies")]
    [Description("Gets direct dependencies for an asset path.")]
    public DependencyEdge[] GetDependencies(
        [Description("Asset path relative to project root, e.g. 'Content/Foo.uasset'")] string assetPath,
        [Description("Maximum number of results")] int limit = 100)
    {
        return _dependencyGraph.GetDependencies(assetPath, limit);
    }

    [KernelFunction("get_dependents")]
    [Description("Gets direct dependents (reverse edges) for an asset path.")]
    public DependencyEdge[] GetDependents(
        [Description("Asset path relative to project root")] string assetPath,
        [Description("Maximum number of results")] int limit = 100)
    {
        return _dependencyGraph.GetDependents(assetPath, limit);
    }

    [KernelFunction("get_related_assets")]
    [Description("Gets related assets around a starting asset path using bounded graph traversal.")]
    public string[] GetRelatedAssets(
        [Description("Asset path relative to project root")] string assetPath,
        [Description("Traversal depth")] int maxDepth = 3,
        [Description("Maximum number of results")] int limit = 200)
    {
        return _dependencyGraph.GetRelated(assetPath, maxDepth, limit);
    }

    [KernelFunction("search_assets_by_class")]
    [Description("Searches project assets by class name.")]
    public ClassSearchHit[] SearchAssetsByClass(
        [Description("Class name to search for")] string className,
        [Description("Maximum number of results")] int limit = 100)
    {
        return _dependencyGraph.SearchByClass(className, limit);
    }

    [KernelFunction("search_asset_properties")]
    [Description("Searches asset properties by property name and optional value filter.")]
    public PropertySearchHit[] SearchAssetProperties(
        [Description("Property name to search for")] string propertyName,
        [Description("Optional value filter")] string? valueFilter = null,
        [Description("Maximum number of results")] int limit = 100)
    {
        return _dependencyGraph.SearchProperties(propertyName, valueFilter, limit);
    }
}
