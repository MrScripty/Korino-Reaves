namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a single actor extracted from a UE level.
/// Sent to frontend for the scene outliner.
/// </summary>
public record SceneActor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("className")] string ClassName,
    [property: JsonPropertyName("meshPath")] string? MeshPath,
    [property: JsonPropertyName("position")] float[]? Position,
    [property: JsonPropertyName("hasMesh")] bool HasMesh,
    [property: JsonPropertyName("isLoaded")] bool IsLoaded,
    [property: JsonPropertyName("levelName")] string LevelName
);
