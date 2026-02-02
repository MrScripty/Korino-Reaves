namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Current selection and expansion state.
/// C# owns this state; frontend reflects it.
/// </summary>
/// <param name="SelectedId">Currently selected node ID, or null if nothing selected</param>
/// <param name="ExpandedIds">IDs of all expanded nodes in the tree</param>
/// <param name="FocusedPropertyPath">Currently focused property path, if any</param>
public record SelectionState(
    [property: JsonPropertyName("selectedId")] string? SelectedId,
    [property: JsonPropertyName("expandedIds")] string[] ExpandedIds,
    [property: JsonPropertyName("focusedPropertyPath")] string[]? FocusedPropertyPath = null
);
