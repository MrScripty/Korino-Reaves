// Asset Plugin - Semantic Kernel functions for asset operations
//
// Exposes asset loading, saving, and info retrieval to the AI agent.

using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using UAssetViewer.Models;
using UAssetViewer.Services;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for asset file operations.
/// </summary>
public sealed class AssetPlugin
{
    private readonly IAssetService _assetService;

    public AssetPlugin(IAssetService assetService)
    {
        _assetService = assetService;
    }

    [KernelFunction("open_asset")]
    [Description("Opens a .uasset file for viewing and editing. Returns asset information.")]
    public async Task<AssetInfo> OpenAsset(
        [Description("Path to the .uasset file")] string path)
    {
        return await _assetService.LoadAsync(path).ConfigureAwait(false);
    }

    [KernelFunction("get_asset_info")]
    [Description("Gets information about the currently loaded asset including file path, engine version, and export/import counts.")]
    public AssetInfo? GetAssetInfo()
    {
        return _assetService.CurrentAsset;
    }

    [KernelFunction("is_asset_loaded")]
    [Description("Checks whether an asset file is currently loaded.")]
    public bool IsAssetLoaded()
    {
        return _assetService.IsLoaded;
    }

    [KernelFunction("save_asset")]
    [Description("Saves the currently loaded asset to disk.")]
    public async Task SaveAsset()
    {
        await _assetService.SaveAsync().ConfigureAwait(false);
    }

    [KernelFunction("save_asset_as")]
    [Description("Saves the currently loaded asset to a new file path.")]
    public async Task SaveAssetAs(
        [Description("New path to save the asset")] string path)
    {
        await _assetService.SaveAsAsync(path).ConfigureAwait(false);
    }

    [KernelFunction("export_json")]
    [Description("Exports the current asset to a JSON file for inspection.")]
    public async Task ExportJson(
        [Description("Path for the JSON output file")] string path)
    {
        await _assetService.ExportJsonAsync(path).ConfigureAwait(false);
    }

    [KernelFunction("close_asset")]
    [Description("Closes the currently loaded asset.")]
    public void CloseAsset()
    {
        _assetService.Close();
    }
}
