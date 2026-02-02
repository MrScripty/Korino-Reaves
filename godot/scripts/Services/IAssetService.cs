// Asset Service Interface
//
// Defines the contract for asset loading and manipulation.
// Implementations will use UAssetAPI for actual operations.

using System.Threading.Tasks;
using UAssetViewer.Models;

namespace UAssetViewer.Services;

/// <summary>
/// Service interface for asset operations.
/// Implementations are Godot-agnostic and can be unit tested.
/// </summary>
public interface IAssetService
{
    /// <summary>
    /// Gets whether an asset is currently loaded.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Gets information about the currently loaded asset.
    /// </summary>
    AssetInfo? CurrentAsset { get; }

    /// <summary>
    /// Loads an asset from the specified path.
    /// </summary>
    /// <param name="path">Path to the .uasset file</param>
    /// <returns>Asset information</returns>
    Task<AssetInfo> LoadAsync(string path);

    /// <summary>
    /// Saves the current asset to its original path.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Saves the current asset to a new path.
    /// </summary>
    /// <param name="path">New path for the asset</param>
    Task SaveAsAsync(string path);

    /// <summary>
    /// Closes the current asset.
    /// </summary>
    void Close();

    /// <summary>
    /// Exports the asset to JSON format.
    /// </summary>
    /// <param name="path">Path for the JSON output</param>
    Task ExportJsonAsync(string path);

    /// <summary>
    /// Imports property values from JSON.
    /// </summary>
    /// <param name="path">Path to the JSON file</param>
    Task ImportJsonAsync(string path);
}
