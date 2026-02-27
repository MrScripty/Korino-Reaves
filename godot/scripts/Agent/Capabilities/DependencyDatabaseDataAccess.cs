// Dependency Database Data Access
//
// SQLite-backed implementation of dependency/metadata queries.

using System;
using System.Linq;
using System.Threading;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Uses <see cref="DependencyDatabase"/> as the backing data source.
/// </summary>
public sealed class DependencyDatabaseDataAccess : IDependencyDataAccess
{
    private readonly IAppLogger _logger;

    public DependencyDatabaseDataAccess(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public DependencyGraphStats GetStats(string projectPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!DependencyDatabase.Exists(projectPath))
        {
            return new DependencyGraphStats(false);
        }

        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        var stats = db.GetStats();
        ct.ThrowIfCancellationRequested();
        if (stats == null)
        {
            return new DependencyGraphStats(true);
        }

        return new DependencyGraphStats(
            Exists: true,
            AssetCount: stats.AssetCount,
            EdgeCount: stats.EdgeCount,
            EngineVersion: stats.EngineVersion,
            ScannedAt: stats.ScannedAt);
    }

    /// <inheritdoc />
    public DependencyEdge[] GetDependencies(string projectPath, string assetPath, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        return db.GetDependencies(assetPath)
            .Take(limit)
            .Select(d => new DependencyEdge(d.Path, d.RefType))
            .ToArray();
    }

    /// <inheritdoc />
    public DependencyEdge[] GetDependents(string projectPath, string assetPath, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        return db.GetDependents(assetPath)
            .Take(limit)
            .Select(d => new DependencyEdge(d.Path, d.RefType))
            .ToArray();
    }

    /// <inheritdoc />
    public string[] GetRelated(string projectPath, string assetPath, int maxDepth, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        return db.GetRelatedCluster(assetPath, maxDepth)
            .Take(limit)
            .ToArray();
    }

    /// <inheritdoc />
    public ClassSearchHit[] SearchByClass(string projectPath, string className, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        return db.SearchByClassName(className, limit)
            .Select(hit => new ClassSearchHit(
                hit.AssetPath,
                hit.Export.ExportIndex,
                hit.Export.ObjectName,
                hit.Export.ClassName))
            .ToArray();
    }

    /// <inheritdoc />
    public PropertySearchHit[] SearchProperties(
        string projectPath,
        string propertyName,
        string? valueFilter,
        int limit,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        return db.SearchProperties(propertyName, valueFilter, limit)
            .Select(hit => new PropertySearchHit(
                hit.AssetPath,
                hit.ExportName,
                hit.Property.Name,
                hit.Property.PropertyType,
                hit.Property.ValueText))
            .ToArray();
    }

    /// <inheritdoc />
    public AssetMetadataSnapshot? GetAssetMetadata(string projectPath, string assetPath, int rowLimit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var db = OpenDatabase(projectPath);
        ct.ThrowIfCancellationRequested();
        var info = db.GetAssetInfo(assetPath);
        ct.ThrowIfCancellationRequested();
        if (info == null)
        {
            return null;
        }

        var imports = db.GetImports(assetPath);
        ct.ThrowIfCancellationRequested();
        var exports = db.GetExports(assetPath);
        ct.ThrowIfCancellationRequested();
        var properties = db.GetAllProperties(assetPath);
        ct.ThrowIfCancellationRequested();
        var edges = db.GetEdges(assetPath);
        ct.ThrowIfCancellationRequested();

        var summary = new AssetMetadataSummary(
            AssetPath: info.Path,
            AssetType: info.AssetType,
            ImportCount: imports.Count,
            ExportCount: exports.Count,
            PropertyCount: properties.Count,
            EdgeCount: edges.Count);

        return new AssetMetadataSnapshot(
            summary,
            imports
                .Take(rowLimit)
                .Select(i => new MetadataImport(i.ImportIndex, i.ObjectName, i.ClassName, i.PackageName, i.IsOptional))
                .ToArray(),
            exports
                .Take(rowLimit)
                .Select(e => new MetadataExport(e.ExportIndex, e.ObjectName, e.ClassName, e.SuperName, e.SerialSize))
                .ToArray(),
            properties
                .Take(rowLimit)
                .Select(p => new MetadataProperty(
                    p.ExportIndex,
                    p.ExportName,
                    p.Name,
                    p.PropertyType,
                    p.ValueText,
                    p.ValueInt,
                    p.ValueFloat,
                    p.ValueRef))
                .ToArray(),
            edges
                .Take(rowLimit)
                .Select(e => new MetadataEdge(e.TargetPath, e.RefType))
                .ToArray());
    }

    private DependencyDatabase OpenDatabase(string projectPath)
    {
        var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen)
        {
            db.Dispose();
            throw new InvalidOperationException("Dependency database is not available. Run dependency scan first.");
        }

        return db;
    }
}
