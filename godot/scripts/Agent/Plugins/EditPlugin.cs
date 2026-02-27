// Edit Plugin - Semantic Kernel functions for property editing
//
// Exposes property read/write operations to the AI agent.

using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using UAssetViewer.Agent;
using UAssetViewer.Assets;
using UAssetViewer.Models;
using UAssetViewer.Services;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for reading and writing asset properties.
/// </summary>
public sealed class EditPlugin
{
    private readonly PropertyService _propertyService;
    private readonly IAssetService _assetService;
    private readonly AgentExecutionPolicy _policy;

    public EditPlugin(PropertyService propertyService, IAssetService assetService, AgentExecutionPolicy policy)
    {
        _propertyService = propertyService;
        _assetService = assetService;
        _policy = policy;
    }

    [KernelFunction("get_property")]
    [Description("Gets the value of a property by its path. Path format: 'export-0/PropertyName' or 'export-0/PropertyName/SubProperty'.")]
    public string GetProperty(
        [Description("Property path using '/' separator, e.g. 'export-0/Health'")] string path)
    {
        var pathSegments = path.Split('/');
        var asset = GetLoadedAsset();
        var value = _propertyService.GetValue(asset, pathSegments);
        return JsonSerializer.Serialize(value);
    }

    [KernelFunction("set_property")]
    [Description("Sets the value of a property. Supports int, float, bool, and string values.")]
    public void SetProperty(
        [Description("Property path using '/' separator")] string path,
        [Description("New value to set (as string, will be converted to appropriate type)")] string value)
    {
        _policy.EnsurePropertyEditsAllowed("set_property");
        var pathSegments = path.Split('/');
        var asset = GetLoadedAsset();
        _propertyService.SetValue(asset, pathSegments, value);
    }

    [KernelFunction("get_all_properties")]
    [Description("Gets all properties for an export by its node ID. Returns property names, types, and values.")]
    public string GetAllProperties(
        [Description("Node ID of the export, e.g. 'export-0'")] string nodeId)
    {
        var asset = GetLoadedAsset();
        var properties = _propertyService.GetPropertiesForNode(asset, nodeId);
        return JsonSerializer.Serialize(properties);
    }

    private UAssetAPI.UAsset GetLoadedAsset()
    {
        if (!_assetService.IsLoaded || _assetService.CurrentAsset == null)
        {
            throw new InvalidOperationException("No asset is currently loaded. Use open_asset first.");
        }

        // The AssetManager exposes the underlying UAsset via its own API.
        // This cast assumes the IAssetService implementation holds the UAsset.
        if (_assetService is AssetManager manager)
        {
            return manager.CurrentUAsset
                ?? throw new InvalidOperationException("Asset is loaded but UAsset instance is null.");
        }

        throw new InvalidOperationException("Asset service does not expose underlying UAsset.");
    }
}
