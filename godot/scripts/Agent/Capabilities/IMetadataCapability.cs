// Metadata Capability
//
// Capability contract for querying bounded asset metadata snapshots.

using System.Threading;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability for asset metadata queries.
/// </summary>
public interface IMetadataCapability
{
    /// <summary>
    /// Gets a bounded metadata snapshot for an asset path in the current project.
    /// </summary>
    AssetMetadataSnapshot? GetAssetMetadata(string assetPath, int rowLimit = 200, CancellationToken ct = default);
}
