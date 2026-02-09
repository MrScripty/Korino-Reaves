// Property Service - Property Read/Write Operations
//
// Handles reading and writing property values by path.
// Converts between UAssetAPI property types and UI-friendly representations.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Assets;

/// <summary>
/// Service for reading and writing property values.
/// </summary>
public sealed class PropertyService
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Assets.Property");

    private readonly IAppLogger _logger;

    public PropertyService(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all properties for a tree node (export).
    /// </summary>
    public PropertyValue[] GetPropertiesForNode(UAsset asset, string nodeId)
    {
        using var activity = ActivitySource.StartActivity("GetPropertiesForNode");
        activity?.SetTag("node.id", nodeId);

        if (!nodeId.StartsWith("export-"))
        {
            return Array.Empty<PropertyValue>();
        }

        // Parse export index
        if (!int.TryParse(nodeId.AsSpan(7), out int exportIndex))
        {
            return Array.Empty<PropertyValue>();
        }

        if (exportIndex < 0 || exportIndex >= asset.Exports.Count)
        {
            return Array.Empty<PropertyValue>();
        }

        var export = asset.Exports[exportIndex];
        if (export is not NormalExport normalExport)
        {
            return Array.Empty<PropertyValue>();
        }

        var properties = new List<PropertyValue>();

        foreach (var prop in normalExport.Data)
        {
            var path = new[] { nodeId, prop.Name.Value.Value };
            properties.Add(ConvertPropertyToValue(prop, path, asset));
        }

        return properties.ToArray();
    }

    /// <summary>
    /// Gets a property value by path.
    /// </summary>
    public object? GetValue(UAsset asset, string[] path)
    {
        using var activity = ActivitySource.StartActivity("GetValue");
        activity?.SetTag("property.path", string.Join("/", path));

        var property = NavigateToProperty(asset, path);
        if (property == null)
        {
            throw new PropertyNotFoundException(path);
        }

        return ExtractValue(property, asset);
    }

    /// <summary>
    /// Sets a property value by path.
    /// </summary>
    public void SetValue(UAsset asset, string[] path, object value)
    {
        using var activity = ActivitySource.StartActivity("SetValue");
        activity?.SetTag("property.path", string.Join("/", path));

        var property = NavigateToProperty(asset, path);
        if (property == null)
        {
            throw new PropertyNotFoundException(path);
        }

        _logger.Info("Setting property value: {Path} = {Value}",
            string.Join("/", path), value);

        ApplyValue(property, value, path);
    }

    private PropertyData? NavigateToProperty(UAsset asset, string[] path)
    {
        if (path.Length < 2)
        {
            return null;
        }

        // First element should be export reference
        var first = path[0];
        if (!first.StartsWith("export-"))
        {
            return null;
        }

        if (!int.TryParse(first.AsSpan(7), out int exportIndex))
        {
            return null;
        }

        if (exportIndex < 0 || exportIndex >= asset.Exports.Count)
        {
            return null;
        }

        var export = asset.Exports[exportIndex];
        if (export is not NormalExport normalExport)
        {
            return null;
        }

        // Navigate through the property path
        PropertyData? current = null;

        for (int i = 1; i < path.Length; i++)
        {
            var segment = path[i];

            if (current == null)
            {
                // Looking for property in export's data
                current = normalExport.Data.FirstOrDefault(
                    p => p.Name.Value.Value == segment);
            }
            else
            {
                // Navigate into current property
                current = NavigateIntoProperty(current, segment);
            }

            if (current == null)
            {
                break;
            }
        }

        return current;
    }

    private static PropertyData? NavigateIntoProperty(PropertyData property, string segment)
    {
        return property switch
        {
            StructPropertyData structProp =>
                structProp.Value.FirstOrDefault(p => p.Name.Value.Value == segment),

            ArrayPropertyData arrayProp when int.TryParse(segment, out int index) =>
                index >= 0 && index < arrayProp.Value.Length ? arrayProp.Value[index] : null,

            MapPropertyData mapProp when segment == "key" || segment == "value" =>
                null, // Map entries need special handling

            _ => null
        };
    }

    private PropertyValue ConvertPropertyToValue(PropertyData property, string[] path, UAsset asset)
    {
        var type = MapPropertyType(property);
        var value = ExtractValue(property, asset);
        var displayName = property.Name.Value.Value;
        var editable = IsEditable(property);
        var metadata = BuildMetadata(property);
        var children = ExtractChildren(property, path, asset);

        return new PropertyValue(
            Path: path,
            Type: type,
            Value: value,
            Editable: editable,
            DisplayName: displayName,
            Metadata: metadata,
            Children: children
        );
    }

    private PropertyValue[]? ExtractChildren(PropertyData property, string[] parentPath, UAsset asset)
    {
        switch (property)
        {
            case StructPropertyData structProp:
            {
                // Skip special struct types that have dedicated editors
                var structType = structProp.StructType.Value.Value;
                if (structType is "Vector" or "Vector2D" or "Vector4" or "Rotator"
                    or "Color" or "LinearColor" or "Guid")
                    return null;

                if (structProp.Value.Count == 0)
                    return null;

                var children = new PropertyValue[structProp.Value.Count];
                for (int i = 0; i < structProp.Value.Count; i++)
                {
                    var child = structProp.Value[i];
                    var childPath = new string[parentPath.Length + 1];
                    Array.Copy(parentPath, childPath, parentPath.Length);
                    childPath[parentPath.Length] = child.Name.Value.Value;
                    children[i] = ConvertPropertyToValue(child, childPath, asset);
                }
                return children;
            }

            case ArrayPropertyData arrayProp:
            {
                if (arrayProp.Value.Length == 0)
                    return null;

                var children = new PropertyValue[arrayProp.Value.Length];
                for (int i = 0; i < arrayProp.Value.Length; i++)
                {
                    var element = arrayProp.Value[i];
                    var childPath = new string[parentPath.Length + 1];
                    Array.Copy(parentPath, childPath, parentPath.Length);
                    childPath[parentPath.Length] = i.ToString();
                    children[i] = ConvertPropertyToValue(element, childPath, asset) with
                    {
                        DisplayName = $"[{i}]"
                    };
                }
                return children;
            }

            case MapPropertyData mapProp:
            {
                if (mapProp.Value.Count == 0)
                    return null;

                var children = new List<PropertyValue>();
                int entryIndex = 0;
                foreach (var kvp in mapProp.Value)
                {
                    var entryPath = new string[parentPath.Length + 1];
                    Array.Copy(parentPath, entryPath, parentPath.Length);
                    entryPath[parentPath.Length] = entryIndex.ToString();

                    // Extract key display string
                    var keyDisplay = ExtractValue(kvp.Key, asset)?.ToString() ?? entryIndex.ToString();

                    // Convert value as the child, using key as display name
                    var valueChild = ConvertPropertyToValue(kvp.Value, entryPath, asset) with
                    {
                        DisplayName = keyDisplay
                    };
                    children.Add(valueChild);
                    entryIndex++;
                }
                return children.ToArray();
            }

            default:
                return null;
        }
    }

    private static string MapPropertyType(PropertyData property)
    {
        return property switch
        {
            IntPropertyData or Int8PropertyData or Int16PropertyData
                or Int64PropertyData or UInt16PropertyData or UInt32PropertyData
                or UInt64PropertyData => PropertyTypes.Number,

            FloatPropertyData or DoublePropertyData => PropertyTypes.Number,

            BoolPropertyData => PropertyTypes.Bool,

            StrPropertyData or NamePropertyData or TextPropertyData => PropertyTypes.String,

            BytePropertyData byteProp => byteProp.ByteType == BytePropertyType.FName
                ? PropertyTypes.Enum
                : PropertyTypes.Byte,

            EnumPropertyData => PropertyTypes.Enum,

            ObjectPropertyData or SoftObjectPropertyData => PropertyTypes.Object,

            StructPropertyData structProp => structProp.StructType.Value.Value switch
            {
                "Vector" or "Vector2D" or "Vector4" or "Rotator" => PropertyTypes.Vector,
                "Color" or "LinearColor" => PropertyTypes.Color,
                "Guid" => PropertyTypes.Guid,
                _ => PropertyTypes.Struct
            },

            ArrayPropertyData or SetPropertyData => PropertyTypes.Array,

            MapPropertyData => PropertyTypes.Map,

            _ => PropertyTypes.Unknown
        };
    }

    private static object? ExtractValue(PropertyData property, UAsset asset)
    {
        return property switch
        {
            IntPropertyData intProp => intProp.Value,
            Int8PropertyData int8Prop => int8Prop.Value,
            Int16PropertyData int16Prop => int16Prop.Value,
            Int64PropertyData int64Prop => int64Prop.Value,
            UInt16PropertyData uint16Prop => uint16Prop.Value,
            UInt32PropertyData uint32Prop => uint32Prop.Value,
            UInt64PropertyData uint64Prop => uint64Prop.Value,

            FloatPropertyData floatProp => floatProp.Value,
            DoublePropertyData doubleProp => doubleProp.Value,

            BoolPropertyData boolProp => boolProp.Value,

            StrPropertyData strProp => strProp.Value?.Value,
            NamePropertyData nameProp => nameProp.Value.Value.Value,
            TextPropertyData textProp => textProp.Value.ToString(),

            BytePropertyData byteProp => byteProp.ByteType == BytePropertyType.FName
                ? byteProp.EnumValue.Value.Value
                : (object)byteProp.Value,

            EnumPropertyData enumProp => enumProp.Value.Value.Value,

            ObjectPropertyData objProp when objProp.Value.Index == 0 => "None",
            ObjectPropertyData objProp when objProp.Value.IsImport() =>
                ResolveImportRef(objProp.Value, asset),
            ObjectPropertyData objProp when objProp.Value.IsExport() =>
                ResolveExportRef(objProp.Value, asset),
            ObjectPropertyData objProp => new { Name = $"Unknown (index: {objProp.Value.Index})", RefType = "unknown" },

            SoftObjectPropertyData softObjProp => new
            {
                AssetPath = softObjProp.Value.AssetPath.AssetName.Value.Value,
                SubPath = softObjProp.Value.SubPathString?.Value
            },

            StructPropertyData structProp => ExtractStructValue(structProp),

            ArrayPropertyData arrayProp => arrayProp.Value.Length,
            MapPropertyData mapProp => mapProp.Value.Count,

            _ => null
        };
    }

    private static object ResolveImportRef(FPackageIndex index, UAsset asset)
    {
        try
        {
            var import = index.ToImport(asset);
            return new
            {
                Name = import.ObjectName.Value.Value,
                Class = import.ClassName.Value.Value,
                RefType = "import"
            };
        }
        catch
        {
            return new { Name = $"Invalid (index: {index.Index})", RefType = "import" };
        }
    }

    private static object ResolveExportRef(FPackageIndex index, UAsset asset)
    {
        try
        {
            var export = index.ToExport(asset);
            return new
            {
                Name = export.ObjectName.Value.Value,
                RefType = "export"
            };
        }
        catch
        {
            return new { Name = $"Invalid (index: {index.Index})", RefType = "export" };
        }
    }

    private static object? ExtractStructValue(StructPropertyData structProp)
    {
        var structType = structProp.StructType.Value.Value;

        // Special handling for common struct types
        return structType switch
        {
            "Vector" when structProp.Value.Count >= 3 =>
                ExtractVectorFromProperties(structProp.Value),

            "Vector2D" when structProp.Value.Count >= 2 =>
                ExtractVector2DFromProperties(structProp.Value),

            "Rotator" when structProp.Value.Count >= 3 =>
                ExtractRotatorFromProperties(structProp.Value),

            "Color" => ExtractColorFromProperties(structProp.Value),

            "LinearColor" => ExtractLinearColorFromProperties(structProp.Value),

            "Guid" when structProp.Value.Count >= 4 =>
                ExtractGuidFromProperties(structProp.Value),

            _ => new { Type = structType, PropertyCount = structProp.Value.Count }
        };
    }

    private static object ExtractVectorFromProperties(List<PropertyData> props)
    {
        float x = 0, y = 0, z = 0;

        foreach (var prop in props)
        {
            var name = prop.Name.Value.Value;
            if (prop is FloatPropertyData floatProp)
            {
                switch (name)
                {
                    case "X": x = floatProp.Value; break;
                    case "Y": y = floatProp.Value; break;
                    case "Z": z = floatProp.Value; break;
                }
            }
            else if (prop is DoublePropertyData doubleProp)
            {
                switch (name)
                {
                    case "X": x = (float)doubleProp.Value; break;
                    case "Y": y = (float)doubleProp.Value; break;
                    case "Z": z = (float)doubleProp.Value; break;
                }
            }
        }

        return new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["z"] = z };
    }

    private static object ExtractVector2DFromProperties(List<PropertyData> props)
    {
        float x = 0, y = 0;

        foreach (var prop in props)
        {
            var name = prop.Name.Value.Value;
            if (prop is FloatPropertyData floatProp)
            {
                switch (name)
                {
                    case "X": x = floatProp.Value; break;
                    case "Y": y = floatProp.Value; break;
                }
            }
        }

        return new Dictionary<string, object> { ["x"] = x, ["y"] = y };
    }

    private static object ExtractRotatorFromProperties(List<PropertyData> props)
    {
        float pitch = 0, yaw = 0, roll = 0;

        foreach (var prop in props)
        {
            var name = prop.Name.Value.Value;
            if (prop is FloatPropertyData floatProp)
            {
                switch (name)
                {
                    case "Pitch": pitch = floatProp.Value; break;
                    case "Yaw": yaw = floatProp.Value; break;
                    case "Roll": roll = floatProp.Value; break;
                }
            }
        }

        return new Dictionary<string, object> { ["x"] = pitch, ["y"] = yaw, ["z"] = roll };
    }

    private static object ExtractColorFromProperties(List<PropertyData> props)
    {
        byte r = 0, g = 0, b = 0, a = 255;

        foreach (var prop in props)
        {
            var name = prop.Name.Value.Value;
            if (prop is BytePropertyData byteProp)
            {
                switch (name)
                {
                    case "R": r = byteProp.Value; break;
                    case "G": g = byteProp.Value; break;
                    case "B": b = byteProp.Value; break;
                    case "A": a = byteProp.Value; break;
                }
            }
        }

        return new Dictionary<string, object> { ["r"] = (int)r, ["g"] = (int)g, ["b"] = (int)b, ["a"] = (int)a };
    }

    private static object ExtractLinearColorFromProperties(List<PropertyData> props)
    {
        float r = 0, g = 0, b = 0, a = 1;

        foreach (var prop in props)
        {
            var name = prop.Name.Value.Value;
            if (prop is FloatPropertyData floatProp)
            {
                switch (name)
                {
                    case "R": r = floatProp.Value; break;
                    case "G": g = floatProp.Value; break;
                    case "B": b = floatProp.Value; break;
                    case "A": a = floatProp.Value; break;
                }
            }
        }

        return new Dictionary<string, object> { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = a };
    }

    private static object ExtractGuidFromProperties(List<PropertyData> props)
    {
        uint a = 0, b = 0, c = 0, d = 0;

        foreach (var prop in props)
        {
            var name = prop.Name.Value.Value;
            if (prop is UInt32PropertyData uint32Prop)
            {
                switch (name)
                {
                    case "A": a = uint32Prop.Value; break;
                    case "B": b = uint32Prop.Value; break;
                    case "C": c = uint32Prop.Value; break;
                    case "D": d = uint32Prop.Value; break;
                }
            }
        }

        var guid = new Guid((int)a, (short)(b >> 16), (short)(b & 0xFFFF),
            (byte)(c >> 24), (byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF),
            (byte)(d >> 24), (byte)((d >> 16) & 0xFF), (byte)((d >> 8) & 0xFF), (byte)(d & 0xFF));
        return guid.ToString();
    }

    private static bool IsEditable(PropertyData property)
    {
        if (property is StructPropertyData structProp)
        {
            // Allow editing for struct types that have frontend editors
            var structType = structProp.StructType.Value.Value;
            return structType is "Vector" or "Vector2D" or "Vector4" or "Rotator"
                or "Color" or "LinearColor";
        }

        // Container types (array, map, set) have no editor
        return property is not (ArrayPropertyData or MapPropertyData or SetPropertyData);
    }

    private PropertyMetadata? BuildMetadata(PropertyData property)
    {
        return property switch
        {
            BytePropertyData byteProp when byteProp.ByteType == BytePropertyType.FName =>
                new PropertyMetadata(
                    UeTypeName: "ByteProperty",
                    EnumValues: Array.Empty<string>() // Would need asset context to get enum values
                ),

            EnumPropertyData enumProp =>
                new PropertyMetadata(
                    UeTypeName: enumProp.EnumType.Value.Value,
                    EnumValues: Array.Empty<string>()
                ),

            StructPropertyData structProp =>
                new PropertyMetadata(
                    UeTypeName: "StructProperty",
                    StructType: structProp.StructType.Value.Value
                ),

            ArrayPropertyData arrayProp =>
                new PropertyMetadata(
                    UeTypeName: "ArrayProperty",
                    ElementType: arrayProp.ArrayType.Value.Value
                ),

            ObjectPropertyData =>
                new PropertyMetadata(UeTypeName: "ObjectProperty"),

            _ => new PropertyMetadata(UeTypeName: property.PropertyType.Value)
        };
    }

    private void ApplyValue(PropertyData property, object value, string[] path)
    {
        try
        {
            switch (property)
            {
                case IntPropertyData intProp:
                    intProp.Value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    break;

                case Int64PropertyData int64Prop:
                    int64Prop.Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    break;

                case FloatPropertyData floatProp:
                    floatProp.Value = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    break;

                case DoublePropertyData doubleProp:
                    doubleProp.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    break;

                case BoolPropertyData boolProp:
                    boolProp.Value = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;

                case StrPropertyData strProp:
                    strProp.Value = new FString(value?.ToString());
                    break;

                case NamePropertyData nameProp:
                    nameProp.Value = FName.DefineDummy(null, value?.ToString() ?? "None");
                    break;

                case BytePropertyData byteProp when byteProp.ByteType == BytePropertyType.Byte:
                    byteProp.Value = Convert.ToByte(value, CultureInfo.InvariantCulture);
                    break;

                case BytePropertyData byteProp when byteProp.ByteType == BytePropertyType.FName:
                    byteProp.EnumValue = FName.DefineDummy(null, value?.ToString() ?? "None");
                    break;

                case EnumPropertyData enumProp:
                    enumProp.Value = FName.DefineDummy(null, value?.ToString() ?? "None");
                    break;

                default:
                    throw new InvalidPropertyValueException(path, value,
                        $"Cannot set value on property type: {property.PropertyType.Value}");
            }
        }
        catch (Exception ex) when (ex is not InvalidPropertyValueException)
        {
            throw new InvalidPropertyValueException(path, value, ex.Message);
        }
    }
}
