// Metadata Plugin - Semantic Kernel functions for asset metadata snapshots
//
// Exposes bounded metadata queries over the dependency database.

using System.ComponentModel;
using Microsoft.SemanticKernel;
using UAssetViewer.Agent.Capabilities;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for asset metadata queries.
/// </summary>
public sealed class MetadataPlugin
{
    private readonly IMetadataCapability _metadata;

    public MetadataPlugin(IMetadataCapability metadata)
    {
        _metadata = metadata;
    }

    [KernelFunction("get_asset_metadata")]
    [Description("Gets a bounded metadata snapshot (summary/imports/exports/properties/edges) for an asset path.")]
    public AssetMetadataSnapshot? GetAssetMetadata(
        [Description("Asset path relative to project root")] string assetPath,
        [Description("Maximum rows per table in the snapshot")] int rowLimit = 200)
    {
        return _metadata.GetAssetMetadata(assetPath, rowLimit);
    }
}
