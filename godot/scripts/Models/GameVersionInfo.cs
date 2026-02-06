// Game Version Info - CUE4Parse EGame models for IPC
//
// Record types for communicating EGame version information
// between the C# backend and the Svelte frontend.

using System.Text.Json.Serialization;

namespace UAssetViewer.Models;

/// <summary>
/// A single EGame enum entry for display in the frontend game selector.
/// </summary>
public sealed record GameVersionEntry(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("group")] string Group
);

/// <summary>
/// Current game version selection state for a project.
/// </summary>
public sealed record GameVersionState(
    [property: JsonPropertyName("selected")] string Selected,
    [property: JsonPropertyName("autoDetected")] string AutoDetected,
    [property: JsonPropertyName("isAutoDetect")] bool IsAutoDetect
);
