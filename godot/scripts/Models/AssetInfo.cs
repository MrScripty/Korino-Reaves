namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Summary information about a loaded asset.
/// </summary>
/// <param name="FilePath">File path of the loaded asset</param>
/// <param name="FileName">Asset file name</param>
/// <param name="EngineVersion">Detected Unreal Engine version</param>
/// <param name="ExportCount">Number of exports in the asset</param>
/// <param name="ImportCount">Number of imports in the asset</param>
/// <param name="NameCount">Number of names in the name map</param>
/// <param name="IsModified">Whether the asset has been modified</param>
/// <param name="AssetClass">Asset class name (if determinable)</param>
public record AssetInfo(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("engineVersion")] string EngineVersion,
    [property: JsonPropertyName("exportCount")] int ExportCount,
    [property: JsonPropertyName("importCount")] int ImportCount,
    [property: JsonPropertyName("nameCount")] int NameCount,
    [property: JsonPropertyName("isModified")] bool IsModified,
    [property: JsonPropertyName("assetClass")] string? AssetClass = null
);

/// <summary>
/// Request to open an asset file.
/// </summary>
/// <param name="FilePath">Path to the asset file</param>
/// <param name="MappingsPath">Optional mappings file path (.usmap)</param>
public record OpenAssetRequest(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("mappingsPath")] string? MappingsPath = null
);
