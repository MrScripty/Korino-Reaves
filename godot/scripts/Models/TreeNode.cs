namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Node type identifiers for color coding in the tree view.
/// Maps to semantic colors defined in the design system.
/// </summary>
public static class TreeNodeTypes
{
    public const string Export = "export";
    public const string Property = "property";
    public const string Array = "array";
    public const string Struct = "struct";
    public const string Map = "map";
    public const string Import = "import";
    public const string Name = "name";
    public const string Header = "header";
    public const string Folder = "folder";
    public const string File = "file";
    public const string Unknown = "unknown";
}

/// <summary>
/// Optional metadata attached to tree nodes for enhanced display.
/// </summary>
/// <param name="ValuePreview">Value preview for leaf nodes (e.g., "100", "true")</param>
/// <param name="TypeName">Type name for display (e.g., "IntProperty", "StrProperty")</param>
/// <param name="ClassName">Export class name if applicable</param>
/// <param name="ArrayIndex">Array index if this node is an array element</param>
/// <param name="IsModified">Whether this node represents a modified value (for diff highlighting)</param>
public record TreeNodeMetadata(
    [property: JsonPropertyName("valuePreview")] string? ValuePreview = null,
    [property: JsonPropertyName("typeName")] string? TypeName = null,
    [property: JsonPropertyName("className")] string? ClassName = null,
    [property: JsonPropertyName("arrayIndex")] int? ArrayIndex = null,
    [property: JsonPropertyName("isModified")] bool? IsModified = null
);

/// <summary>
/// Represents a single node in the asset tree.
/// Used for both display and navigation.
/// </summary>
/// <param name="Id">Unique identifier for this node (path-based)</param>
/// <param name="Name">Display name shown in tree</param>
/// <param name="Type">Node type for color coding and icon selection</param>
/// <param name="HasChildren">Whether this node can be expanded</param>
/// <param name="Children">Child nodes (populated on expand, null when collapsed)</param>
/// <param name="Metadata">Additional metadata for display</param>
public record TreeNode(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("hasChildren")] bool HasChildren,
    [property: JsonPropertyName("children")] TreeNode[]? Children = null,
    [property: JsonPropertyName("metadata")] TreeNodeMetadata? Metadata = null
);
