// Tree Builder - Asset Tree Structure
//
// Builds a navigable tree structure from UAsset exports, imports, and properties.
// Supports lazy loading of children for large assets.
// Pattern derived from UAssetGUI's TableHandler approach.

using System;
using System.Collections.Generic;
using System.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Assets;

/// <summary>
/// Builds tree structure from UAsset for display in the UI.
/// </summary>
public sealed class TreeBuilder
{
    private static readonly HashSet<string> ContainerPropertyTypes = new()
    {
        "StructProperty",
        "ArrayProperty",
        "SetProperty",
        "MapProperty",
        "GameplayTagContainer",
        "MulticastDelegateProperty"
    };

    private readonly IAppLogger _logger;
    private UAsset? _asset;
    private readonly Dictionary<string, object> _nodePointers = new();
    private readonly Dictionary<string, string[]> _nodePaths = new();

    public TreeBuilder(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the tree builder with an asset.
    /// </summary>
    public void Initialize(UAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _nodePointers.Clear();
        _nodePaths.Clear();
        _logger.Debug("TreeBuilder initialized with asset");
    }

    /// <summary>
    /// Clears the tree builder state.
    /// </summary>
    public void Clear()
    {
        _asset = null;
        _nodePointers.Clear();
        _nodePaths.Clear();
    }

    /// <summary>
    /// Gets the root-level tree nodes.
    /// </summary>
    public TreeNode[] GetRootNodes()
    {
        if (_asset == null)
        {
            return Array.Empty<TreeNode>();
        }

        var nodes = new List<TreeNode>();

        // General Information
        nodes.Add(CreateNode("general", "General Information", TreeNodeTypes.Header, false));

        // Name Map
        nodes.Add(CreateNode("names", "Name Map", TreeNodeTypes.Header,
            _asset.GetNameMapIndexList().Count > 0));

        // Imports
        nodes.Add(CreateNode("imports", "Imports", TreeNodeTypes.Header,
            _asset.Imports.Count > 0));

        // Exports
        nodes.Add(CreateNode("exports", "Exports", TreeNodeTypes.Header,
            _asset.Exports.Count > 0));

        _logger.Debug("Built {Count} root nodes", nodes.Count);
        return nodes.ToArray();
    }

    /// <summary>
    /// Gets the children of a specific node.
    /// </summary>
    public TreeNode[] GetChildren(string nodeId)
    {
        if (_asset == null)
        {
            return Array.Empty<TreeNode>();
        }

        _logger.Debug("Getting children for node: {NodeId}", nodeId);

        return nodeId switch
        {
            "names" => GetNameMapNodes(),
            "imports" => GetImportNodes(),
            "exports" => GetExportNodes(),
            _ when nodeId.StartsWith("export-") => GetExportChildNodes(nodeId),
            _ when nodeId.StartsWith("property-") => GetPropertyChildNodes(nodeId),
            _ => Array.Empty<TreeNode>()
        };
    }

    /// <summary>
    /// Searches for nodes matching the query.
    /// </summary>
    public TreeNode[] Search(string query)
    {
        if (_asset == null || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<TreeNode>();
        }

        var results = new List<TreeNode>();
        var lowerQuery = query.ToLowerInvariant();

        // Search exports
        for (int i = 0; i < _asset.Exports.Count; i++)
        {
            var export = _asset.Exports[i];
            var name = export.ObjectName.Value.Value;

            if (name.ToLowerInvariant().Contains(lowerQuery))
            {
                results.Add(CreateExportNode(i, export));
            }
        }

        // Search names
        var names = _asset.GetNameMapIndexList();
        foreach (var name in names)
        {
            if (name.Value.ToLowerInvariant().Contains(lowerQuery))
            {
                results.Add(CreateNode(
                    $"name-{name.Value}",
                    name.Value,
                    TreeNodeTypes.Name,
                    false
                ));
            }
        }

        _logger.Debug("Search found {Count} results for query: {Query}", results.Count, query);
        return results.ToArray();
    }

    /// <summary>
    /// Gets the path from root to a specific node.
    /// </summary>
    public string[] GetPathToNode(string nodeId)
    {
        if (_nodePaths.TryGetValue(nodeId, out var path))
        {
            return path;
        }

        // Build path by parsing the node ID
        var parts = new List<string>();

        if (nodeId.StartsWith("export-"))
        {
            parts.Add("exports");
            parts.Add(nodeId);
        }
        else if (nodeId.StartsWith("import-"))
        {
            parts.Add("imports");
            parts.Add(nodeId);
        }
        else if (nodeId.StartsWith("name-"))
        {
            parts.Add("names");
            parts.Add(nodeId);
        }
        else if (nodeId.StartsWith("property-"))
        {
            // Parse property path
            var segments = nodeId.Split('/');
            if (segments.Length > 0)
            {
                parts.Add("exports");
                parts.AddRange(segments);
            }
        }

        return parts.ToArray();
    }

    /// <summary>
    /// Invalidates a node's cached children.
    /// </summary>
    public void InvalidateNode(string nodeId)
    {
        // Remove cached pointers for this node and all children
        var keysToRemove = _nodePointers.Keys
            .Where(k => k.StartsWith(nodeId))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _nodePointers.Remove(key);
            _nodePaths.Remove(key);
        }
    }

    private TreeNode[] GetNameMapNodes()
    {
        if (_asset == null) return Array.Empty<TreeNode>();

        var names = _asset.GetNameMapIndexList();
        var nodes = new List<TreeNode>();

        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            nodes.Add(CreateNode(
                $"name-{i}",
                $"[{i}] {name.Value}",
                TreeNodeTypes.Name,
                false,
                new TreeNodeMetadata(ValuePreview: name.Value)
            ));
        }

        return nodes.ToArray();
    }

    private TreeNode[] GetImportNodes()
    {
        if (_asset == null) return Array.Empty<TreeNode>();

        var nodes = new List<TreeNode>();

        for (int i = 0; i < _asset.Imports.Count; i++)
        {
            var import = _asset.Imports[i];
            var name = import.ObjectName.Value.Value;
            var className = import.ClassName.Value.Value;

            nodes.Add(CreateNode(
                $"import-{i}",
                $"Import[{i}]: {name}",
                TreeNodeTypes.Import,
                false,
                new TreeNodeMetadata(ClassName: className)
            ));
        }

        return nodes.ToArray();
    }

    private TreeNode[] GetExportNodes()
    {
        if (_asset == null) return Array.Empty<TreeNode>();

        var nodes = new List<TreeNode>();

        for (int i = 0; i < _asset.Exports.Count; i++)
        {
            var export = _asset.Exports[i];
            nodes.Add(CreateExportNode(i, export));
        }

        return nodes.ToArray();
    }

    private TreeNode CreateExportNode(int index, Export export)
    {
        var name = export.ObjectName.Value.Value;
        string? className = null;

        if (export.ClassIndex.IsImport())
        {
            var import = export.ClassIndex.ToImport(_asset);
            className = import?.ObjectName.Value.Value;
        }

        var hasChildren = export is NormalExport normalExport && normalExport.Data.Count > 0;
        var nodeId = $"export-{index}";

        _nodePointers[nodeId] = export;
        _nodePaths[nodeId] = new[] { "exports", nodeId };

        return CreateNode(
            nodeId,
            $"Export[{index}]: {name}",
            TreeNodeTypes.Export,
            hasChildren,
            new TreeNodeMetadata(ClassName: className)
        );
    }

    private TreeNode[] GetExportChildNodes(string nodeId)
    {
        if (_asset == null) return Array.Empty<TreeNode>();

        // Parse export index from nodeId (e.g., "export-0")
        if (!int.TryParse(nodeId.AsSpan(7), out int exportIndex))
        {
            return Array.Empty<TreeNode>();
        }

        if (exportIndex < 0 || exportIndex >= _asset.Exports.Count)
        {
            return Array.Empty<TreeNode>();
        }

        var export = _asset.Exports[exportIndex];

        if (export is not NormalExport normalExport)
        {
            // Raw export - just show size
            return new[]
            {
                CreateNode(
                    $"{nodeId}/raw",
                    $"Raw Data ({((RawExport)export).Data.Length} bytes)",
                    TreeNodeTypes.Unknown,
                    false
                )
            };
        }

        var nodes = new List<TreeNode>();

        for (int i = 0; i < normalExport.Data.Count; i++)
        {
            var property = normalExport.Data[i];
            var propNode = CreatePropertyNode(nodeId, i, property);
            nodes.Add(propNode);
        }

        return nodes.ToArray();
    }

    private TreeNode CreatePropertyNode(string parentId, int index, PropertyData property)
    {
        var propertyType = property.PropertyType.Value.Value;
        var name = property.Name.Value.Value;
        var nodeId = $"{parentId}/property-{index}-{name}";

        _nodePointers[nodeId] = property;

        var hasChildren = ContainerPropertyTypes.Contains(propertyType);
        var valuePreview = GetPropertyValuePreview(property);
        var treeNodeType = GetTreeNodeTypeForProperty(propertyType);

        return CreateNode(
            nodeId,
            name,
            treeNodeType,
            hasChildren,
            new TreeNodeMetadata(
                ValuePreview: valuePreview,
                TypeName: propertyType
            )
        );
    }

    private TreeNode[] GetPropertyChildNodes(string nodeId)
    {
        if (!_nodePointers.TryGetValue(nodeId, out var pointer))
        {
            return Array.Empty<TreeNode>();
        }

        if (pointer is not PropertyData property)
        {
            return Array.Empty<TreeNode>();
        }

        return property switch
        {
            StructPropertyData structProp => GetStructChildren(nodeId, structProp),
            ArrayPropertyData arrayProp => GetArrayChildren(nodeId, arrayProp),
            MapPropertyData mapProp => GetMapChildren(nodeId, mapProp),
            _ => Array.Empty<TreeNode>()
        };
    }

    private TreeNode[] GetStructChildren(string parentId, StructPropertyData structProp)
    {
        var nodes = new List<TreeNode>();

        for (int i = 0; i < structProp.Value.Count; i++)
        {
            var child = structProp.Value[i];
            nodes.Add(CreatePropertyNode(parentId, i, child));
        }

        return nodes.ToArray();
    }

    private TreeNode[] GetArrayChildren(string parentId, ArrayPropertyData arrayProp)
    {
        var nodes = new List<TreeNode>();

        for (int i = 0; i < arrayProp.Value.Length; i++)
        {
            var element = arrayProp.Value[i];
            var nodeId = $"{parentId}/element-{i}";
            var elementType = element.PropertyType.Value.Value;

            _nodePointers[nodeId] = element;

            var hasChildren = ContainerPropertyTypes.Contains(elementType);
            var valuePreview = GetPropertyValuePreview(element);

            nodes.Add(CreateNode(
                nodeId,
                $"[{i}]",
                TreeNodeTypes.Array,
                hasChildren,
                new TreeNodeMetadata(
                    ValuePreview: valuePreview,
                    TypeName: elementType,
                    ArrayIndex: i
                )
            ));
        }

        return nodes.ToArray();
    }

    private TreeNode[] GetMapChildren(string parentId, MapPropertyData mapProp)
    {
        var nodes = new List<TreeNode>();
        int i = 0;

        foreach (var entry in mapProp.Value)
        {
            var entryId = $"{parentId}/entry-{i}";

            // Key node
            var keyId = $"{entryId}/key";
            _nodePointers[keyId] = entry.Key;
            nodes.Add(CreatePropertyNode(entryId, 0, entry.Key));

            // Value node
            var valueId = $"{entryId}/value";
            _nodePointers[valueId] = entry.Value;
            nodes.Add(CreatePropertyNode(entryId, 1, entry.Value));

            i++;
        }

        return nodes.ToArray();
    }

    private static string? GetPropertyValuePreview(PropertyData property)
    {
        return property switch
        {
            IntPropertyData intProp => intProp.Value.ToString(),
            FloatPropertyData floatProp => floatProp.Value.ToString("F3"),
            BoolPropertyData boolProp => boolProp.Value.ToString(),
            StrPropertyData strProp => TruncateString(strProp.Value?.Value, 50),
            NamePropertyData nameProp => nameProp.Value.Value.Value,
            BytePropertyData byteProp => byteProp.ByteType switch
            {
                BytePropertyType.Byte => byteProp.Value.ToString(),
                BytePropertyType.FName => byteProp.EnumValue.Value.Value,
                _ => null
            },
            EnumPropertyData enumProp => enumProp.Value.Value.Value,
            ObjectPropertyData objProp => objProp.Value.Index.ToString(),
            SoftObjectPropertyData softObjProp => softObjProp.Value.AssetPath.AssetName.Value.Value,
            ArrayPropertyData arrProp => $"[{arrProp.Value.Length} elements]",
            StructPropertyData structProp => $"({structProp.Value.Count} properties)",
            MapPropertyData mapProp => $"{{{mapProp.Value.Count} entries}}",
            _ => null
        };
    }

    private static string TruncateString(string? value, int maxLength)
    {
        if (value == null) return "null";
        if (value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 3) + "...";
    }

    private static string GetTreeNodeTypeForProperty(string propertyType)
    {
        return propertyType switch
        {
            "StructProperty" or "ClothLODData" => TreeNodeTypes.Struct,
            "ArrayProperty" or "SetProperty" => TreeNodeTypes.Array,
            "MapProperty" => TreeNodeTypes.Map,
            "ObjectProperty" or "SoftObjectProperty" => TreeNodeTypes.Property,
            _ => TreeNodeTypes.Property
        };
    }

    private static TreeNode CreateNode(
        string id,
        string name,
        string type,
        bool hasChildren,
        TreeNodeMetadata? metadata = null)
    {
        return new TreeNode(
            Id: id,
            Name: name,
            Type: type,
            HasChildren: hasChildren,
            Children: null,
            Metadata: metadata
        );
    }
}
