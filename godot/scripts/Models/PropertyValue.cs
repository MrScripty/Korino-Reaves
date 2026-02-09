namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Property value types corresponding to UE property system.
/// Used for selecting appropriate editors in the UI.
/// </summary>
public static class PropertyTypes
{
    public const string String = "string";
    public const string Number = "number";
    public const string Bool = "bool";
    public const string Vector = "vector";
    public const string Color = "color";
    public const string Enum = "enum";
    public const string Object = "object";
    public const string Struct = "struct";
    public const string Array = "array";
    public const string Map = "map";
    public const string Set = "set";
    public const string Byte = "byte";
    public const string Guid = "guid";
    public const string Unknown = "unknown";
}

/// <summary>
/// Type-specific metadata for properties.
/// </summary>
/// <param name="EnumValues">For enums: available enum values</param>
/// <param name="Min">For numbers: minimum value</param>
/// <param name="Max">For numbers: maximum value</param>
/// <param name="ObjectClass">For objects: class restriction</param>
/// <param name="ElementType">For arrays: element type</param>
/// <param name="StructType">For structs: struct type name</param>
/// <param name="UeTypeName">Original UE property type name</param>
public record PropertyMetadata(
    [property: JsonPropertyName("enumValues")] string[]? EnumValues = null,
    [property: JsonPropertyName("min")] double? Min = null,
    [property: JsonPropertyName("max")] double? Max = null,
    [property: JsonPropertyName("objectClass")] string? ObjectClass = null,
    [property: JsonPropertyName("elementType")] string? ElementType = null,
    [property: JsonPropertyName("structType")] string? StructType = null,
    [property: JsonPropertyName("ueTypeName")] string? UeTypeName = null
);

/// <summary>
/// Represents a property value that can be displayed and edited.
/// </summary>
/// <param name="Path">Path from root to this property</param>
/// <param name="Type">Property type for editor selection</param>
/// <param name="Value">Current value (type depends on PropertyType)</param>
/// <param name="Editable">Whether this property can be edited</param>
/// <param name="DisplayName">Display name (last segment of path by default)</param>
/// <param name="Metadata">Type-specific metadata</param>
public record PropertyValue(
    [property: JsonPropertyName("path")] string[] Path,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] object? Value,
    [property: JsonPropertyName("editable")] bool Editable,
    [property: JsonPropertyName("displayName")] string? DisplayName = null,
    [property: JsonPropertyName("metadata")] PropertyMetadata? Metadata = null,
    [property: JsonPropertyName("isEdited")] bool IsEdited = false,
    [property: JsonPropertyName("children")] PropertyValue[]? Children = null
);
