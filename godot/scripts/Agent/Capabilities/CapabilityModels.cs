// Agent Capability Models
//
// Shared models for the agent capability layer. These models are intentionally
// independent from IPC transport types so capabilities can be reused by plugins
// and workflows without coupling to bridge contracts.

using System;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Summary of dependency graph availability and scale.
/// </summary>
public sealed record DependencyGraphStats(
    bool Exists,
    int AssetCount = 0,
    int EdgeCount = 0,
    string? EngineVersion = null,
    DateTime? ScannedAt = null
);

/// <summary>
/// Directed dependency edge for an asset reference.
/// </summary>
public sealed record DependencyEdge(
    string Path,
    string RefType
);

/// <summary>
/// Search hit for class-based asset lookups.
/// </summary>
public sealed record ClassSearchHit(
    string AssetPath,
    int ExportIndex,
    string ObjectName,
    string? ClassName
);

/// <summary>
/// Search hit for property-based lookups.
/// </summary>
public sealed record PropertySearchHit(
    string AssetPath,
    string ExportName,
    string PropertyName,
    string PropertyType,
    string? ValueText
);

/// <summary>
/// High-level metadata counts for an asset.
/// </summary>
public sealed record AssetMetadataSummary(
    string AssetPath,
    string AssetType,
    int ImportCount,
    int ExportCount,
    int PropertyCount,
    int EdgeCount
);

/// <summary>
/// Compact import row used by agent metadata queries.
/// </summary>
public sealed record MetadataImport(
    int ImportIndex,
    string ObjectName,
    string ClassName,
    string? PackageName,
    bool IsOptional
);

/// <summary>
/// Compact export row used by agent metadata queries.
/// </summary>
public sealed record MetadataExport(
    int ExportIndex,
    string ObjectName,
    string? ClassName,
    string? SuperName,
    long SerialSize
);

/// <summary>
/// Compact property row used by agent metadata queries.
/// </summary>
public sealed record MetadataProperty(
    int ExportIndex,
    string ExportName,
    string Name,
    string PropertyType,
    string? ValueText,
    long? ValueInt,
    double? ValueFloat,
    string? ValueRef
);

/// <summary>
/// Compact edge row used by agent metadata queries.
/// </summary>
public sealed record MetadataEdge(
    string TargetPath,
    string RefType
);

/// <summary>
/// Bounded metadata snapshot for a single asset.
/// </summary>
public sealed record AssetMetadataSnapshot(
    AssetMetadataSummary Summary,
    MetadataImport[] Imports,
    MetadataExport[] Exports,
    MetadataProperty[] Properties,
    MetadataEdge[] Edges
);
