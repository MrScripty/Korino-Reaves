// Agent Capability Registry
//
// Central container for capability instances used by agent plugins/workflows.

using System;
using UAssetViewer.Agent.Capabilities;

namespace UAssetViewer.Agent;

/// <summary>
/// Holds initialized capability instances for agent composition.
/// </summary>
public sealed class AgentCapabilityRegistry
{
    public AgentCapabilityRegistry(
        IProjectExplorerCapability projectExplorer,
        IDependencyGraphCapability dependencyGraph,
        IMetadataCapability metadata,
        IGuiSelectionCapability guiSelection)
    {
        ProjectExplorer = projectExplorer ?? throw new ArgumentNullException(nameof(projectExplorer));
        DependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        GuiSelection = guiSelection ?? throw new ArgumentNullException(nameof(guiSelection));
    }

    /// <summary>
    /// Project file exploration capability.
    /// </summary>
    public IProjectExplorerCapability ProjectExplorer { get; }

    /// <summary>
    /// Dependency graph traversal capability.
    /// </summary>
    public IDependencyGraphCapability DependencyGraph { get; }

    /// <summary>
    /// Asset metadata query capability.
    /// </summary>
    public IMetadataCapability Metadata { get; }

    /// <summary>
    /// GUI selection/expansion capability.
    /// </summary>
    public IGuiSelectionCapability GuiSelection { get; }
}
