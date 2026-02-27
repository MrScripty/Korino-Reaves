// Asset Metadata Database - SQLite persistence for full asset metadata
//
// Stores all parsed metadata from .uasset/.umap files: package info,
// imports, exports, properties (recursive), dependency edges, custom
// versions, localization text, searchable names, and world tile info.
// Generated at scan time, queried at runtime. Uses WAL mode for
// lock-free concurrent reads.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Data;

// -----------------------------------------------------------------
// Record types for query results
// -----------------------------------------------------------------

public sealed record DependencyReference(string Path, string RefType);

public sealed record DependencyStats(
    int AssetCount,
    int EdgeCount,
    string? EngineVersion,
    DateTime ScannedAt
);

public sealed record AssetInfo(
    long Id,
    string Path,
    string AssetType,
    string? ObjectVersion,
    string? ObjectVersionUE5,
    bool IsUnversioned,
    int PackageFlags,
    int? EngineMajor,
    int? EngineMinor,
    int? EnginePatch,
    int? EngineChangelist,
    string? EngineBranch,
    int ImportCount,
    int ExportCount
);

public sealed record ImportEntry(
    long Id,
    int ImportIndex,
    string ObjectName,
    string ClassName,
    string? ClassPackage,
    int? OuterIndex,
    string? PackageName,
    bool IsOptional
);

public sealed record ExportEntry(
    long Id,
    int ExportIndex,
    string ObjectName,
    string? ClassName,
    string? SuperName,
    int? OuterIndex,
    int ObjectFlags,
    long SerialSize,
    bool IsAsset,
    bool NotForClient,
    bool NotForServer,
    bool IsForcedExport,
    int ExtrasSize
);

public sealed record PropertyEntry(
    long Id,
    long ExportId,
    long? ParentId,
    int SortOrder,
    string Name,
    string PropertyType,
    string? StructType,
    int ArrayIndex,
    long? ValueInt,
    double? ValueFloat,
    string? ValueText,
    string? ValueRef
);

public sealed record FlatPropertyEntry(
    long Id,
    long ExportId,
    int ExportIndex,
    string ExportName,
    long? ParentId,
    int SortOrder,
    string Name,
    string PropertyType,
    string? StructType,
    int ArrayIndex,
    long? ValueInt,
    double? ValueFloat,
    string? ValueText,
    string? ValueRef
);

public sealed record CustomVersionEntry(
    long Id,
    long AssetId,
    string Guid,
    string? FriendlyName,
    int Version
);

public sealed record ExportDependencyEntry(
    long Id,
    long ExportId,
    int ExportIndex,
    string? ExportName,
    string DepType,
    int TargetIndex
);

public sealed record GatherableTextEntry(
    long Id,
    string? Namespace,
    string? SourceString,
    string? KeyName,
    string? SiteDescription,
    bool IsEditorOnly
);

public sealed record SearchableNameEntry(
    long Id,
    int ExportIndex,
    string Name
);

public sealed record WorldTileInfoEntry(
    long Id,
    int? PositionX,
    int? PositionY,
    int? PositionZ,
    int? AbsPositionX,
    int? AbsPositionY,
    int? AbsPositionZ,
    string? LayerName,
    int? StreamingDistance,
    bool DistanceStreamingEnabled,
    string? ParentTilePackage,
    int? ZOrder,
    bool HideInTileView,
    string? LodListJson
);

public sealed record EdgeEntry(
    long Id,
    string TargetPath,
    string RefType
);

// -----------------------------------------------------------------
// Database
// -----------------------------------------------------------------

/// <summary>
/// SQLite-backed asset metadata database for a project.
/// Stores full metadata from every .uasset/.umap: package info, imports,
/// exports, properties, dependency edges, custom versions, and more.
/// </summary>
public sealed class DependencyDatabase : IDisposable
{
    private readonly IAppLogger _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    // Prepared statements for bulk inserts (created once per scan)
    private SqliteCommand? _insertAssetCmd;
    private SqliteCommand? _insertCustomVersionCmd;
    private SqliteCommand? _insertImportCmd;
    private SqliteCommand? _insertExportCmd;
    private SqliteCommand? _insertExportDepCmd;
    private SqliteCommand? _insertPropertyCmd;
    private SqliteCommand? _insertEdgeCmd;
    private SqliteCommand? _insertGatherableTextCmd;
    private SqliteCommand? _insertSearchableNameCmd;
    private SqliteCommand? _insertWorldTileInfoCmd;

    public bool IsOpen => _connection != null;

    public DependencyDatabase(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new database, dropping any existing one.
    /// Used during scanning to build from scratch.
    /// </summary>
    public void Create(string projectPath)
    {
        Close();

        var dbPath = GetDatabasePath(projectPath);
        var dbDir = Path.GetDirectoryName(dbPath);
        if (dbDir != null && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        // Delete existing DB and orphaned WAL/SHM files to start fresh
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        EnableWal();
        CreateSchema();
        _logger.Info("Asset database created: {Path}", dbPath);
    }

    /// <summary>
    /// Opens or creates the database, preserving existing data.
    /// Used for crash-resilient scanning that can resume after SIGSEGV.
    /// </summary>
    public void CreateOrOpen(string projectPath)
    {
        Close();

        var dbPath = GetDatabasePath(projectPath);
        var dbDir = Path.GetDirectoryName(dbPath);
        if (dbDir != null && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        EnableWal();
        CreateSchemaIfNeeded();
        _logger.Info("Asset database ready: {Path}", dbPath);
    }

    /// <summary>
    /// Gets all asset paths already in the database.
    /// Used to skip re-processing files during a resumed scan.
    /// </summary>
    public HashSet<string> GetExistingAssetPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_connection == null) return paths;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT path FROM assets WHERE object_version IS NOT NULL";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }
        return paths;
    }

    /// <summary>
    /// Updates all prepared insert statements to use a new transaction.
    /// Called after committing a batch and starting a new transaction.
    /// </summary>
    public void UpdatePreparedTransactions(SqliteTransaction transaction)
    {
        if (_insertAssetCmd != null) _insertAssetCmd.Transaction = transaction;
        if (_insertCustomVersionCmd != null) _insertCustomVersionCmd.Transaction = transaction;
        if (_insertImportCmd != null) _insertImportCmd.Transaction = transaction;
        if (_insertExportCmd != null) _insertExportCmd.Transaction = transaction;
        if (_insertExportDepCmd != null) _insertExportDepCmd.Transaction = transaction;
        if (_insertPropertyCmd != null) _insertPropertyCmd.Transaction = transaction;
        if (_insertEdgeCmd != null) _insertEdgeCmd.Transaction = transaction;
        if (_insertGatherableTextCmd != null) _insertGatherableTextCmd.Transaction = transaction;
        if (_insertSearchableNameCmd != null) _insertSearchableNameCmd.Transaction = transaction;
        if (_insertWorldTileInfoCmd != null) _insertWorldTileInfoCmd.Transaction = transaction;
    }

    /// <summary>
    /// Opens an existing database for read-only queries.
    /// </summary>
    public void Open(string projectPath)
    {
        Close();

        var dbPath = GetDatabasePath(projectPath);
        if (!File.Exists(dbPath))
        {
            _logger.Debug("No asset database found at: {Path}", dbPath);
            return;
        }

        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _connection.Open();

        EnableWal();
        _logger.Info("Asset database opened: {Path}", dbPath);
    }

    /// <summary>
    /// Checks whether a database exists for the given project.
    /// </summary>
    public static bool Exists(string projectPath)
    {
        return File.Exists(GetDatabasePath(projectPath));
    }

    // =================================================================
    // Write Operations (used during scanning)
    // =================================================================

    public SqliteTransaction BeginTransaction()
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");
        return _connection.BeginTransaction();
    }

    /// <summary>
    /// Prepares all insert statements for reuse during a scan.
    /// Call once after BeginTransaction, before the scan loop.
    /// The returned transaction must be passed to each insert call.
    /// </summary>
    public void PrepareInsertStatements(SqliteTransaction transaction)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        _insertAssetCmd = CreateInsertAssetCommand(transaction);
        _insertCustomVersionCmd = CreateInsertCustomVersionCommand(transaction);
        _insertImportCmd = CreateInsertImportCommand(transaction);
        _insertExportCmd = CreateInsertExportCommand(transaction);
        _insertExportDepCmd = CreateInsertExportDepCommand(transaction);
        _insertPropertyCmd = CreateInsertPropertyCommand(transaction);
        _insertEdgeCmd = CreateInsertEdgeCommand(transaction);
        _insertGatherableTextCmd = CreateInsertGatherableTextCommand(transaction);
        _insertSearchableNameCmd = CreateInsertSearchableNameCommand(transaction);
        _insertWorldTileInfoCmd = CreateInsertWorldTileInfoCommand(transaction);
    }

    /// <summary>
    /// Disposes prepared statements after a scan completes.
    /// </summary>
    public void DisposeInsertStatements()
    {
        _insertAssetCmd?.Dispose(); _insertAssetCmd = null;
        _insertCustomVersionCmd?.Dispose(); _insertCustomVersionCmd = null;
        _insertImportCmd?.Dispose(); _insertImportCmd = null;
        _insertExportCmd?.Dispose(); _insertExportCmd = null;
        _insertExportDepCmd?.Dispose(); _insertExportDepCmd = null;
        _insertPropertyCmd?.Dispose(); _insertPropertyCmd = null;
        _insertEdgeCmd?.Dispose(); _insertEdgeCmd = null;
        _insertGatherableTextCmd?.Dispose(); _insertGatherableTextCmd = null;
        _insertSearchableNameCmd?.Dispose(); _insertSearchableNameCmd = null;
        _insertWorldTileInfoCmd?.Dispose(); _insertWorldTileInfoCmd = null;
    }

    // -- Assets -------------------------------------------------------

    public long InsertAsset(
        string path, string assetType,
        string? objectVersion, string? objectVersionUE5,
        bool isUnversioned, int packageFlags,
        int? engineMajor, int? engineMinor, int? enginePatch,
        int? engineChangelist, string? engineBranch,
        int importCount, int exportCount,
        long? payloadTocOffset, int? dataResourceOffset,
        string? savedHash)
    {
        var cmd = _insertAssetCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$path"].Value = path;
        cmd.Parameters["$assetType"].Value = assetType;
        cmd.Parameters["$objectVersion"].Value = (object?)objectVersion ?? DBNull.Value;
        cmd.Parameters["$objectVersionUE5"].Value = (object?)objectVersionUE5 ?? DBNull.Value;
        cmd.Parameters["$isUnversioned"].Value = isUnversioned ? 1 : 0;
        cmd.Parameters["$packageFlags"].Value = packageFlags;
        cmd.Parameters["$engineMajor"].Value = (object?)engineMajor ?? DBNull.Value;
        cmd.Parameters["$engineMinor"].Value = (object?)engineMinor ?? DBNull.Value;
        cmd.Parameters["$enginePatch"].Value = (object?)enginePatch ?? DBNull.Value;
        cmd.Parameters["$engineChangelist"].Value = (object?)engineChangelist ?? DBNull.Value;
        cmd.Parameters["$engineBranch"].Value = (object?)engineBranch ?? DBNull.Value;
        cmd.Parameters["$importCount"].Value = importCount;
        cmd.Parameters["$exportCount"].Value = exportCount;
        cmd.Parameters["$payloadTocOffset"].Value = (object?)payloadTocOffset ?? DBNull.Value;
        cmd.Parameters["$dataResourceOffset"].Value = (object?)dataResourceOffset ?? DBNull.Value;
        cmd.Parameters["$savedHash"].Value = (object?)savedHash ?? DBNull.Value;

        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Inserts an asset node with minimal info (for dependency targets
    /// that haven't been scanned yet). Returns the asset ID.
    /// </summary>
    public long InsertAssetMinimal(string path, string assetType, SqliteTransaction? transaction = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO assets (path, asset_type) VALUES ($path, $type);
            SELECT id FROM assets WHERE path = $path";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$type", assetType);

        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Gets the asset ID for a path, or null if not found.
    /// </summary>
    public long? GetAssetId(string path)
    {
        if (_connection == null) return null;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM assets WHERE path = $path";
        cmd.Parameters.AddWithValue("$path", path);

        var result = cmd.ExecuteScalar();
        return result != null ? (long)result : null;
    }

    // -- Custom Versions ----------------------------------------------

    public void InsertCustomVersion(long assetId, string guid, string? friendlyName, int version)
    {
        var cmd = _insertCustomVersionCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$assetId"].Value = assetId;
        cmd.Parameters["$guid"].Value = guid;
        cmd.Parameters["$friendlyName"].Value = (object?)friendlyName ?? DBNull.Value;
        cmd.Parameters["$version"].Value = version;

        cmd.ExecuteNonQuery();
    }

    // -- Imports -------------------------------------------------------

    public void InsertImport(
        long assetId, int importIndex,
        string objectName, string className, string? classPackage,
        int? outerIndex, string? packageName, bool isOptional)
    {
        var cmd = _insertImportCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$assetId"].Value = assetId;
        cmd.Parameters["$importIndex"].Value = importIndex;
        cmd.Parameters["$objectName"].Value = objectName;
        cmd.Parameters["$className"].Value = className;
        cmd.Parameters["$classPackage"].Value = (object?)classPackage ?? DBNull.Value;
        cmd.Parameters["$outerIndex"].Value = (object?)outerIndex ?? DBNull.Value;
        cmd.Parameters["$packageName"].Value = (object?)packageName ?? DBNull.Value;
        cmd.Parameters["$isOptional"].Value = isOptional ? 1 : 0;

        cmd.ExecuteNonQuery();
    }

    // -- Exports -------------------------------------------------------

    public long InsertExport(
        long assetId, int exportIndex,
        string objectName, string? className, string? superName,
        int? outerIndex, int objectFlags, long serialSize,
        bool isAsset, bool notForClient, bool notForServer,
        bool isForcedExport, int extrasSize)
    {
        var cmd = _insertExportCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$assetId"].Value = assetId;
        cmd.Parameters["$exportIndex"].Value = exportIndex;
        cmd.Parameters["$objectName"].Value = objectName;
        cmd.Parameters["$className"].Value = (object?)className ?? DBNull.Value;
        cmd.Parameters["$superName"].Value = (object?)superName ?? DBNull.Value;
        cmd.Parameters["$outerIndex"].Value = (object?)outerIndex ?? DBNull.Value;
        cmd.Parameters["$objectFlags"].Value = objectFlags;
        cmd.Parameters["$serialSize"].Value = serialSize;
        cmd.Parameters["$isAsset"].Value = isAsset ? 1 : 0;
        cmd.Parameters["$notForClient"].Value = notForClient ? 1 : 0;
        cmd.Parameters["$notForServer"].Value = notForServer ? 1 : 0;
        cmd.Parameters["$isForcedExport"].Value = isForcedExport ? 1 : 0;
        cmd.Parameters["$extrasSize"].Value = extrasSize;

        return (long)cmd.ExecuteScalar()!;
    }

    // -- Export Dependencies -------------------------------------------

    public void InsertExportDependency(long exportId, string depType, int targetIndex)
    {
        var cmd = _insertExportDepCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$exportId"].Value = exportId;
        cmd.Parameters["$depType"].Value = depType;
        cmd.Parameters["$targetIndex"].Value = targetIndex;

        cmd.ExecuteNonQuery();
    }

    // -- Properties ----------------------------------------------------

    public long InsertProperty(
        long exportId, long? parentId, int sortOrder,
        string name, string propertyType, string? structType,
        int arrayIndex,
        long? valueInt, double? valueFloat,
        string? valueText, string? valueRef)
    {
        var cmd = _insertPropertyCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$exportId"].Value = exportId;
        cmd.Parameters["$parentId"].Value = (object?)parentId ?? DBNull.Value;
        cmd.Parameters["$sortOrder"].Value = sortOrder;
        cmd.Parameters["$name"].Value = name;
        cmd.Parameters["$propertyType"].Value = propertyType;
        cmd.Parameters["$structType"].Value = (object?)structType ?? DBNull.Value;
        cmd.Parameters["$arrayIndex"].Value = arrayIndex;
        cmd.Parameters["$valueInt"].Value = (object?)valueInt ?? DBNull.Value;
        cmd.Parameters["$valueFloat"].Value = (object?)valueFloat ?? DBNull.Value;
        cmd.Parameters["$valueText"].Value = (object?)valueText ?? DBNull.Value;
        cmd.Parameters["$valueRef"].Value = (object?)valueRef ?? DBNull.Value;

        return (long)cmd.ExecuteScalar()!;
    }

    // -- Edges ---------------------------------------------------------

    public void InsertEdge(long sourceId, long targetId, string refType)
    {
        var cmd = _insertEdgeCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$source"].Value = sourceId;
        cmd.Parameters["$target"].Value = targetId;
        cmd.Parameters["$refType"].Value = refType;

        cmd.ExecuteNonQuery();
    }

    // -- Gatherable Text -----------------------------------------------

    public void InsertGatherableText(
        long assetId, string? ns, string? sourceString,
        string? keyName, string? siteDescription, bool isEditorOnly)
    {
        var cmd = _insertGatherableTextCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$assetId"].Value = assetId;
        cmd.Parameters["$namespace"].Value = (object?)ns ?? DBNull.Value;
        cmd.Parameters["$sourceString"].Value = (object?)sourceString ?? DBNull.Value;
        cmd.Parameters["$keyName"].Value = (object?)keyName ?? DBNull.Value;
        cmd.Parameters["$siteDescription"].Value = (object?)siteDescription ?? DBNull.Value;
        cmd.Parameters["$isEditorOnly"].Value = isEditorOnly ? 1 : 0;

        cmd.ExecuteNonQuery();
    }

    // -- Searchable Names ----------------------------------------------

    public void InsertSearchableName(long assetId, int exportIndex, string name)
    {
        var cmd = _insertSearchableNameCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$assetId"].Value = assetId;
        cmd.Parameters["$exportIndex"].Value = exportIndex;
        cmd.Parameters["$name"].Value = name;

        cmd.ExecuteNonQuery();
    }

    // -- World Tile Info -----------------------------------------------

    public void InsertWorldTileInfo(
        long assetId,
        int? posX, int? posY, int? posZ,
        int? absPosX, int? absPosY, int? absPosZ,
        string? layerName, int? streamingDistance,
        bool distanceStreamingEnabled,
        string? parentTilePackage, int? zOrder,
        bool hideInTileView, string? lodListJson)
    {
        var cmd = _insertWorldTileInfoCmd ?? throw new InvalidOperationException("Prepared statements not initialized");
        cmd.Parameters["$assetId"].Value = assetId;
        cmd.Parameters["$posX"].Value = (object?)posX ?? DBNull.Value;
        cmd.Parameters["$posY"].Value = (object?)posY ?? DBNull.Value;
        cmd.Parameters["$posZ"].Value = (object?)posZ ?? DBNull.Value;
        cmd.Parameters["$absPosX"].Value = (object?)absPosX ?? DBNull.Value;
        cmd.Parameters["$absPosY"].Value = (object?)absPosY ?? DBNull.Value;
        cmd.Parameters["$absPosZ"].Value = (object?)absPosZ ?? DBNull.Value;
        cmd.Parameters["$layerName"].Value = (object?)layerName ?? DBNull.Value;
        cmd.Parameters["$streamingDistance"].Value = (object?)streamingDistance ?? DBNull.Value;
        cmd.Parameters["$distStreamEnabled"].Value = distanceStreamingEnabled ? 1 : 0;
        cmd.Parameters["$parentTilePackage"].Value = (object?)parentTilePackage ?? DBNull.Value;
        cmd.Parameters["$zOrder"].Value = (object?)zOrder ?? DBNull.Value;
        cmd.Parameters["$hideInTileView"].Value = hideInTileView ? 1 : 0;
        cmd.Parameters["$lodListJson"].Value = (object?)lodListJson ?? DBNull.Value;

        cmd.ExecuteNonQuery();
    }

    // -- Meta ----------------------------------------------------------

    public void SetMeta(string key, string value, SqliteTransaction? transaction = null)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);

        cmd.ExecuteNonQuery();
    }

    // =================================================================
    // Read Operations (used at runtime)
    // =================================================================

    /// <summary>
    /// Gets package-level metadata for an asset.
    /// </summary>
    public AssetInfo? GetAssetInfo(string assetPath)
    {
        if (_connection == null) return null;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, path, asset_type, object_version, object_version_ue5,
                   is_unversioned, package_flags,
                   engine_major, engine_minor, engine_patch,
                   engine_changelist, engine_branch,
                   import_count, export_count
            FROM assets WHERE path = $path";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new AssetInfo(
            Id: r.GetInt64(0),
            Path: r.GetString(1),
            AssetType: r.GetString(2),
            ObjectVersion: r.IsDBNull(3) ? null : r.GetString(3),
            ObjectVersionUE5: r.IsDBNull(4) ? null : r.GetString(4),
            IsUnversioned: !r.IsDBNull(5) && r.GetInt32(5) != 0,
            PackageFlags: r.IsDBNull(6) ? 0 : r.GetInt32(6),
            EngineMajor: r.IsDBNull(7) ? null : r.GetInt32(7),
            EngineMinor: r.IsDBNull(8) ? null : r.GetInt32(8),
            EnginePatch: r.IsDBNull(9) ? null : r.GetInt32(9),
            EngineChangelist: r.IsDBNull(10) ? null : r.GetInt32(10),
            EngineBranch: r.IsDBNull(11) ? null : r.GetString(11),
            ImportCount: r.GetInt32(12),
            ExportCount: r.GetInt32(13)
        );
    }

    /// <summary>
    /// Gets all imports for an asset.
    /// </summary>
    public List<ImportEntry> GetImports(string assetPath)
    {
        var results = new List<ImportEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT i.id, i.import_index, i.object_name, i.class_name,
                   i.class_package, i.outer_index, i.package_name, i.is_optional
            FROM imports i
            JOIN assets a ON i.asset_id = a.id
            WHERE a.path = $path
            ORDER BY i.import_index";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new ImportEntry(
                Id: r.GetInt64(0),
                ImportIndex: r.GetInt32(1),
                ObjectName: r.GetString(2),
                ClassName: r.GetString(3),
                ClassPackage: r.IsDBNull(4) ? null : r.GetString(4),
                OuterIndex: r.IsDBNull(5) ? null : r.GetInt32(5),
                PackageName: r.IsDBNull(6) ? null : r.GetString(6),
                IsOptional: !r.IsDBNull(7) && r.GetInt32(7) != 0
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets all exports for an asset.
    /// </summary>
    public List<ExportEntry> GetExports(string assetPath)
    {
        var results = new List<ExportEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT e.id, e.export_index, e.object_name, e.class_name,
                   e.super_name, e.outer_index, e.object_flags, e.serial_size,
                   e.is_asset, e.not_for_client, e.not_for_server,
                   e.is_forced_export, e.extras_size
            FROM exports e
            JOIN assets a ON e.asset_id = a.id
            WHERE a.path = $path
            ORDER BY e.export_index";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new ExportEntry(
                Id: r.GetInt64(0),
                ExportIndex: r.GetInt32(1),
                ObjectName: r.GetString(2),
                ClassName: r.IsDBNull(3) ? null : r.GetString(3),
                SuperName: r.IsDBNull(4) ? null : r.GetString(4),
                OuterIndex: r.IsDBNull(5) ? null : r.GetInt32(5),
                ObjectFlags: r.IsDBNull(6) ? 0 : r.GetInt32(6),
                SerialSize: r.IsDBNull(7) ? 0 : r.GetInt64(7),
                IsAsset: !r.IsDBNull(8) && r.GetInt32(8) != 0,
                NotForClient: !r.IsDBNull(9) && r.GetInt32(9) != 0,
                NotForServer: !r.IsDBNull(10) && r.GetInt32(10) != 0,
                IsForcedExport: !r.IsDBNull(11) && r.GetInt32(11) != 0,
                ExtrasSize: r.IsDBNull(12) ? 0 : r.GetInt32(12)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets the property tree for an export.
    /// Returns a flat list; use ParentId to reconstruct the hierarchy.
    /// </summary>
    public List<PropertyEntry> GetProperties(long exportId)
    {
        var results = new List<PropertyEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, export_id, parent_id, sort_order,
                   name, property_type, struct_type, array_index,
                   value_int, value_float, value_text, value_ref
            FROM properties
            WHERE export_id = $exportId
            ORDER BY parent_id NULLS FIRST, sort_order";
        cmd.Parameters.AddWithValue("$exportId", exportId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new PropertyEntry(
                Id: r.GetInt64(0),
                ExportId: r.GetInt64(1),
                ParentId: r.IsDBNull(2) ? null : r.GetInt64(2),
                SortOrder: r.GetInt32(3),
                Name: r.GetString(4),
                PropertyType: r.GetString(5),
                StructType: r.IsDBNull(6) ? null : r.GetString(6),
                ArrayIndex: r.IsDBNull(7) ? 0 : r.GetInt32(7),
                ValueInt: r.IsDBNull(8) ? null : r.GetInt64(8),
                ValueFloat: r.IsDBNull(9) ? null : r.GetDouble(9),
                ValueText: r.IsDBNull(10) ? null : r.GetString(10),
                ValueRef: r.IsDBNull(11) ? null : r.GetString(11)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets all properties for all exports of an asset, with export context.
    /// </summary>
    public List<FlatPropertyEntry> GetAllProperties(string assetPath)
    {
        var results = new List<FlatPropertyEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT p.id, p.export_id, e.export_index, e.object_name,
                   p.parent_id, p.sort_order,
                   p.name, p.property_type, p.struct_type, p.array_index,
                   p.value_int, p.value_float, p.value_text, p.value_ref
            FROM properties p
            JOIN exports e ON p.export_id = e.id
            JOIN assets a ON e.asset_id = a.id
            WHERE a.path = $path
            ORDER BY e.export_index, p.parent_id NULLS FIRST, p.sort_order";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new FlatPropertyEntry(
                Id: r.GetInt64(0),
                ExportId: r.GetInt64(1),
                ExportIndex: r.GetInt32(2),
                ExportName: r.GetString(3),
                ParentId: r.IsDBNull(4) ? null : r.GetInt64(4),
                SortOrder: r.GetInt32(5),
                Name: r.GetString(6),
                PropertyType: r.GetString(7),
                StructType: r.IsDBNull(8) ? null : r.GetString(8),
                ArrayIndex: r.IsDBNull(9) ? 0 : r.GetInt32(9),
                ValueInt: r.IsDBNull(10) ? null : r.GetInt64(10),
                ValueFloat: r.IsDBNull(11) ? null : r.GetDouble(11),
                ValueText: r.IsDBNull(12) ? null : r.GetString(12),
                ValueRef: r.IsDBNull(13) ? null : r.GetString(13)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets custom versions for an asset.
    /// </summary>
    public List<CustomVersionEntry> GetCustomVersions(string assetPath)
    {
        var results = new List<CustomVersionEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT cv.id, cv.asset_id, cv.guid, cv.friendly_name, cv.version
            FROM custom_versions cv
            JOIN assets a ON cv.asset_id = a.id
            WHERE a.path = $path
            ORDER BY cv.friendly_name";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new CustomVersionEntry(
                Id: r.GetInt64(0),
                AssetId: r.GetInt64(1),
                Guid: r.GetString(2),
                FriendlyName: r.IsDBNull(3) ? null : r.GetString(3),
                Version: r.GetInt32(4)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets export dependencies for all exports of an asset.
    /// </summary>
    public List<ExportDependencyEntry> GetExportDependencies(string assetPath)
    {
        var results = new List<ExportDependencyEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ed.id, ed.export_id, e.export_index, e.object_name,
                   ed.dep_type, ed.target_index
            FROM export_dependencies ed
            JOIN exports e ON ed.export_id = e.id
            JOIN assets a ON e.asset_id = a.id
            WHERE a.path = $path
            ORDER BY e.export_index, ed.dep_type";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new ExportDependencyEntry(
                Id: r.GetInt64(0),
                ExportId: r.GetInt64(1),
                ExportIndex: r.GetInt32(2),
                ExportName: r.IsDBNull(3) ? null : r.GetString(3),
                DepType: r.GetString(4),
                TargetIndex: r.GetInt32(5)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets gatherable text entries for an asset.
    /// </summary>
    public List<GatherableTextEntry> GetGatherableText(string assetPath)
    {
        var results = new List<GatherableTextEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT gt.id, gt.namespace, gt.source_string,
                   gt.key_name, gt.site_description, gt.is_editor_only
            FROM gatherable_text gt
            JOIN assets a ON gt.asset_id = a.id
            WHERE a.path = $path
            ORDER BY gt.id";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new GatherableTextEntry(
                Id: r.GetInt64(0),
                Namespace: r.IsDBNull(1) ? null : r.GetString(1),
                SourceString: r.IsDBNull(2) ? null : r.GetString(2),
                KeyName: r.IsDBNull(3) ? null : r.GetString(3),
                SiteDescription: r.IsDBNull(4) ? null : r.GetString(4),
                IsEditorOnly: !r.IsDBNull(5) && r.GetInt32(5) != 0
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets searchable names for an asset.
    /// </summary>
    public List<SearchableNameEntry> GetSearchableNames(string assetPath)
    {
        var results = new List<SearchableNameEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT sn.id, sn.export_index, sn.name
            FROM searchable_names sn
            JOIN assets a ON sn.asset_id = a.id
            WHERE a.path = $path
            ORDER BY sn.export_index, sn.name";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new SearchableNameEntry(
                Id: r.GetInt64(0),
                ExportIndex: r.GetInt32(1),
                Name: r.GetString(2)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets world tile info for an asset (maps only).
    /// </summary>
    public List<WorldTileInfoEntry> GetWorldTileInfo(string assetPath)
    {
        var results = new List<WorldTileInfoEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT wti.id,
                   wti.position_x, wti.position_y, wti.position_z,
                   wti.abs_position_x, wti.abs_position_y, wti.abs_position_z,
                   wti.layer_name, wti.streaming_distance,
                   wti.distance_streaming_enabled,
                   wti.parent_tile_package, wti.z_order,
                   wti.hide_in_tile_view, wti.lod_list_json
            FROM world_tile_info wti
            JOIN assets a ON wti.asset_id = a.id
            WHERE a.path = $path";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new WorldTileInfoEntry(
                Id: r.GetInt64(0),
                PositionX: r.IsDBNull(1) ? null : r.GetInt32(1),
                PositionY: r.IsDBNull(2) ? null : r.GetInt32(2),
                PositionZ: r.IsDBNull(3) ? null : r.GetInt32(3),
                AbsPositionX: r.IsDBNull(4) ? null : r.GetInt32(4),
                AbsPositionY: r.IsDBNull(5) ? null : r.GetInt32(5),
                AbsPositionZ: r.IsDBNull(6) ? null : r.GetInt32(6),
                LayerName: r.IsDBNull(7) ? null : r.GetString(7),
                StreamingDistance: r.IsDBNull(8) ? null : r.GetInt32(8),
                DistanceStreamingEnabled: !r.IsDBNull(9) && r.GetInt32(9) != 0,
                ParentTilePackage: r.IsDBNull(10) ? null : r.GetString(10),
                ZOrder: r.IsDBNull(11) ? null : r.GetInt32(11),
                HideInTileView: !r.IsDBNull(12) && r.GetInt32(12) != 0,
                LodListJson: r.IsDBNull(13) ? null : r.GetString(13)
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets outgoing edges for an asset with target paths.
    /// </summary>
    public List<EdgeEntry> GetEdges(string assetPath)
    {
        var results = new List<EdgeEntry>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT e.id, tgt.path, e.ref_type
            FROM edges e
            JOIN assets src ON e.source_id = src.id
            JOIN assets tgt ON e.target_id = tgt.id
            WHERE src.path = $path
            ORDER BY e.ref_type, tgt.path";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new EdgeEntry(
                Id: r.GetInt64(0),
                TargetPath: r.GetString(1),
                RefType: r.GetString(2)
            ));
        }

        return results;
    }

    /// <summary>
    /// Finds exports by class name. Returns asset path + export info.
    /// </summary>
    public List<(string AssetPath, ExportEntry Export)> SearchByClassName(string className, int limit = 100)
    {
        var results = new List<(string, ExportEntry)>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT a.path,
                   e.id, e.export_index, e.object_name, e.class_name,
                   e.super_name, e.outer_index, e.object_flags, e.serial_size,
                   e.is_asset, e.not_for_client, e.not_for_server,
                   e.is_forced_export, e.extras_size
            FROM exports e
            JOIN assets a ON e.asset_id = a.id
            WHERE e.class_name = $className
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$className", className);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var assetPath = r.GetString(0);
            var export = new ExportEntry(
                Id: r.GetInt64(1),
                ExportIndex: r.GetInt32(2),
                ObjectName: r.GetString(3),
                ClassName: r.IsDBNull(4) ? null : r.GetString(4),
                SuperName: r.IsDBNull(5) ? null : r.GetString(5),
                OuterIndex: r.IsDBNull(6) ? null : r.GetInt32(6),
                ObjectFlags: r.IsDBNull(7) ? 0 : r.GetInt32(7),
                SerialSize: r.IsDBNull(8) ? 0 : r.GetInt64(8),
                IsAsset: !r.IsDBNull(9) && r.GetInt32(9) != 0,
                NotForClient: !r.IsDBNull(10) && r.GetInt32(10) != 0,
                NotForServer: !r.IsDBNull(11) && r.GetInt32(11) != 0,
                IsForcedExport: !r.IsDBNull(12) && r.GetInt32(12) != 0,
                ExtrasSize: r.IsDBNull(13) ? 0 : r.GetInt32(13)
            );
            results.Add((assetPath, export));
        }

        return results;
    }

    /// <summary>
    /// Searches properties by name and optional value.
    /// Returns asset path, export name, and property info.
    /// </summary>
    public List<(string AssetPath, string ExportName, PropertyEntry Property)> SearchProperties(
        string propertyName, string? valueFilter = null, int limit = 100)
    {
        var results = new List<(string, string, PropertyEntry)>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        var sql = @"
            SELECT a.path, ex.object_name,
                   p.id, p.export_id, p.parent_id, p.sort_order,
                   p.name, p.property_type, p.struct_type, p.array_index,
                   p.value_int, p.value_float, p.value_text, p.value_ref
            FROM properties p
            JOIN exports ex ON p.export_id = ex.id
            JOIN assets a ON ex.asset_id = a.id
            WHERE p.name = $name";

        if (valueFilter != null)
        {
            sql += " AND (p.value_text = $value OR CAST(p.value_int AS TEXT) = $value OR CAST(p.value_float AS TEXT) = $value)";
            cmd.Parameters.AddWithValue("$value", valueFilter);
        }

        sql += " LIMIT $limit";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$name", propertyName);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var assetPath = r.GetString(0);
            var exportName = r.GetString(1);
            var prop = new PropertyEntry(
                Id: r.GetInt64(2),
                ExportId: r.GetInt64(3),
                ParentId: r.IsDBNull(4) ? null : r.GetInt64(4),
                SortOrder: r.GetInt32(5),
                Name: r.GetString(6),
                PropertyType: r.GetString(7),
                StructType: r.IsDBNull(8) ? null : r.GetString(8),
                ArrayIndex: r.IsDBNull(9) ? 0 : r.GetInt32(9),
                ValueInt: r.IsDBNull(10) ? null : r.GetInt64(10),
                ValueFloat: r.IsDBNull(11) ? null : r.GetDouble(11),
                ValueText: r.IsDBNull(12) ? null : r.GetString(12),
                ValueRef: r.IsDBNull(13) ? null : r.GetString(13)
            );
            results.Add((assetPath, exportName, prop));
        }

        return results;
    }

    /// <summary>
    /// Gets all assets that the given asset depends on (outgoing edges).
    /// </summary>
    public List<DependencyReference> GetDependencies(string assetPath)
    {
        var results = new List<DependencyReference>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT a.path, e.ref_type
            FROM edges e
            JOIN assets a ON e.target_id = a.id
            WHERE e.source_id = (SELECT id FROM assets WHERE path = $path)
            ORDER BY e.ref_type, a.path";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DependencyReference(reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    /// <summary>
    /// Gets all assets that depend on the given asset (incoming edges).
    /// </summary>
    public List<DependencyReference> GetDependents(string assetPath)
    {
        var results = new List<DependencyReference>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT a.path, e.ref_type
            FROM edges e
            JOIN assets a ON e.source_id = a.id
            WHERE e.target_id = (SELECT id FROM assets WHERE path = $path)
            ORDER BY e.ref_type, a.path";
        cmd.Parameters.AddWithValue("$path", assetPath);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DependencyReference(reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    /// <summary>
    /// Gets all unique asset paths that are referenced with the given ref types.
    /// Used by AssetImporter to enumerate all textures/meshes/materials for pre-extraction.
    /// </summary>
    public List<string> GetUniqueTargetsByRefType(params string[] refTypes)
    {
        var results = new List<string>();
        if (_connection == null) return results;
        if (refTypes == null || refTypes.Length == 0) return results;

        using var cmd = _connection.CreateCommand();

        // Build parameterized IN clause dynamically
        var paramNames = new string[refTypes.Length];
        for (int i = 0; i < refTypes.Length; i++)
        {
            paramNames[i] = $"$rt{i}";
            cmd.Parameters.AddWithValue(paramNames[i], refTypes[i]);
        }

        cmd.CommandText = $@"
            SELECT DISTINCT a.path
            FROM edges e
            JOIN assets a ON e.target_id = a.id
            WHERE e.ref_type IN ({string.Join(", ", paramNames)})
            ORDER BY a.path";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    /// <summary>
    /// Gets the transitive closure of related assets (BFS in both directions).
    /// </summary>
    public List<string> GetRelatedCluster(string assetPath, int maxDepth = 3)
    {
        var results = new List<string>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            WITH RECURSIVE related(id, depth) AS (
                SELECT id, 0 FROM assets WHERE path = $path
                UNION
                SELECT e.target_id, r.depth + 1
                FROM edges e JOIN related r ON e.source_id = r.id
                WHERE r.depth < $maxDepth
                UNION
                SELECT e.source_id, r.depth + 1
                FROM edges e JOIN related r ON e.target_id = r.id
                WHERE r.depth < $maxDepth
            )
            SELECT DISTINCT a.path
            FROM related r
            JOIN assets a ON r.id = a.id
            WHERE a.path != $path
            ORDER BY a.path";
        cmd.Parameters.AddWithValue("$path", assetPath);
        cmd.Parameters.AddWithValue("$maxDepth", maxDepth);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    /// <summary>
    /// Gets graph/scan statistics from the meta table.
    /// </summary>
    public DependencyStats? GetStats()
    {
        if (_connection == null) return null;

        var meta = new Dictionary<string, string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM meta";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            meta[reader.GetString(0)] = reader.GetString(1);
        }

        if (meta.Count == 0) return null;

        return new DependencyStats(
            AssetCount: meta.TryGetValue("asset_count", out var ac) ? int.Parse(ac) : 0,
            EdgeCount: meta.TryGetValue("edge_count", out var ec) ? int.Parse(ec) : 0,
            EngineVersion: meta.GetValueOrDefault("engine_version"),
            ScannedAt: meta.TryGetValue("scanned_at", out var sa) ? DateTime.Parse(sa) : DateTime.MinValue
        );
    }

    // =================================================================
    // Lifecycle
    // =================================================================

    public void Close()
    {
        DisposeInsertStatements();

        if (_connection != null)
        {
            _connection.Close();
            _connection.Dispose();
            _connection = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }

    // =================================================================
    // Schema & Config
    // =================================================================

    private void EnableWal()
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL";
        cmd.ExecuteNonQuery();
    }

    private void CreateSchemaIfNeeded()
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS assets (
                id                   INTEGER PRIMARY KEY,
                path                 TEXT NOT NULL UNIQUE,
                asset_type           TEXT NOT NULL,
                object_version       TEXT,
                object_version_ue5   TEXT,
                is_unversioned       INTEGER,
                package_flags        INTEGER,
                engine_major         INTEGER,
                engine_minor         INTEGER,
                engine_patch         INTEGER,
                engine_changelist    INTEGER,
                engine_branch        TEXT,
                import_count         INTEGER NOT NULL DEFAULT 0,
                export_count         INTEGER NOT NULL DEFAULT 0,
                payload_toc_offset   INTEGER,
                data_resource_offset INTEGER,
                saved_hash           TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_assets_path ON assets(path);
            CREATE INDEX IF NOT EXISTS idx_assets_type ON assets(asset_type);
            CREATE TABLE IF NOT EXISTS custom_versions (
                id            INTEGER PRIMARY KEY,
                asset_id      INTEGER NOT NULL REFERENCES assets(id),
                guid          TEXT NOT NULL,
                friendly_name TEXT,
                version       INTEGER NOT NULL,
                UNIQUE(asset_id, guid)
            );
            CREATE INDEX IF NOT EXISTS idx_cv_asset ON custom_versions(asset_id);
            CREATE INDEX IF NOT EXISTS idx_cv_name ON custom_versions(friendly_name);
            CREATE TABLE IF NOT EXISTS imports (
                id            INTEGER PRIMARY KEY,
                asset_id      INTEGER NOT NULL REFERENCES assets(id),
                import_index  INTEGER NOT NULL,
                object_name   TEXT NOT NULL,
                class_name    TEXT NOT NULL,
                class_package TEXT,
                outer_index   INTEGER,
                package_name  TEXT,
                is_optional   INTEGER DEFAULT 0,
                UNIQUE(asset_id, import_index)
            );
            CREATE INDEX IF NOT EXISTS idx_imports_asset ON imports(asset_id);
            CREATE INDEX IF NOT EXISTS idx_imports_object_name ON imports(object_name);
            CREATE INDEX IF NOT EXISTS idx_imports_class_name ON imports(class_name);
            CREATE TABLE IF NOT EXISTS exports (
                id               INTEGER PRIMARY KEY,
                asset_id         INTEGER NOT NULL REFERENCES assets(id),
                export_index     INTEGER NOT NULL,
                object_name      TEXT NOT NULL,
                class_name       TEXT,
                super_name       TEXT,
                outer_index      INTEGER,
                object_flags     INTEGER,
                serial_size      INTEGER,
                is_asset         INTEGER DEFAULT 0,
                not_for_client   INTEGER DEFAULT 0,
                not_for_server   INTEGER DEFAULT 0,
                is_forced_export INTEGER DEFAULT 0,
                extras_size      INTEGER DEFAULT 0,
                UNIQUE(asset_id, export_index)
            );
            CREATE INDEX IF NOT EXISTS idx_exports_asset ON exports(asset_id);
            CREATE INDEX IF NOT EXISTS idx_exports_class_name ON exports(class_name);
            CREATE INDEX IF NOT EXISTS idx_exports_object_name ON exports(object_name);
            CREATE TABLE IF NOT EXISTS export_dependencies (
                id           INTEGER PRIMARY KEY,
                export_id    INTEGER NOT NULL REFERENCES exports(id),
                dep_type     TEXT NOT NULL,
                target_index INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_expdeps_export ON export_dependencies(export_id);
            CREATE TABLE IF NOT EXISTS properties (
                id            INTEGER PRIMARY KEY,
                export_id     INTEGER NOT NULL REFERENCES exports(id),
                parent_id     INTEGER REFERENCES properties(id),
                sort_order    INTEGER NOT NULL DEFAULT 0,
                name          TEXT NOT NULL,
                property_type TEXT NOT NULL,
                struct_type   TEXT,
                array_index   INTEGER DEFAULT 0,
                value_int     INTEGER,
                value_float   REAL,
                value_text    TEXT,
                value_ref     TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_props_export ON properties(export_id);
            CREATE INDEX IF NOT EXISTS idx_props_parent ON properties(parent_id);
            CREATE INDEX IF NOT EXISTS idx_props_name ON properties(name);
            CREATE INDEX IF NOT EXISTS idx_props_name_type ON properties(name, property_type);
            CREATE INDEX IF NOT EXISTS idx_props_value_text ON properties(name, value_text) WHERE value_text IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_props_value_int ON properties(name, value_int) WHERE value_int IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_props_value_ref ON properties(value_ref) WHERE value_ref IS NOT NULL;
            CREATE TABLE IF NOT EXISTS edges (
                id        INTEGER PRIMARY KEY,
                source_id INTEGER NOT NULL REFERENCES assets(id),
                target_id INTEGER NOT NULL REFERENCES assets(id),
                ref_type  TEXT NOT NULL,
                UNIQUE(source_id, target_id, ref_type)
            );
            CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id);
            CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id);
            CREATE TABLE IF NOT EXISTS gatherable_text (
                id               INTEGER PRIMARY KEY,
                asset_id         INTEGER NOT NULL REFERENCES assets(id),
                namespace        TEXT,
                source_string    TEXT,
                key_name         TEXT,
                site_description TEXT,
                is_editor_only   INTEGER DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_gtext_asset ON gatherable_text(asset_id);
            CREATE INDEX IF NOT EXISTS idx_gtext_key ON gatherable_text(key_name) WHERE key_name IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_gtext_source ON gatherable_text(source_string) WHERE source_string IS NOT NULL;
            CREATE TABLE IF NOT EXISTS searchable_names (
                id           INTEGER PRIMARY KEY,
                asset_id     INTEGER NOT NULL REFERENCES assets(id),
                export_index INTEGER NOT NULL,
                name         TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_snames_asset ON searchable_names(asset_id);
            CREATE INDEX IF NOT EXISTS idx_snames_name ON searchable_names(name);
            CREATE TABLE IF NOT EXISTS world_tile_info (
                id                         INTEGER PRIMARY KEY,
                asset_id                   INTEGER NOT NULL REFERENCES assets(id),
                position_x                 INTEGER,
                position_y                 INTEGER,
                position_z                 INTEGER,
                abs_position_x             INTEGER,
                abs_position_y             INTEGER,
                abs_position_z             INTEGER,
                layer_name                 TEXT,
                streaming_distance         INTEGER,
                distance_streaming_enabled INTEGER,
                parent_tile_package        TEXT,
                z_order                    INTEGER,
                hide_in_tile_view          INTEGER DEFAULT 0,
                lod_list_json              TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_wti_asset ON world_tile_info(asset_id);
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private void CreateSchema()
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            -- Package-level metadata per file
            CREATE TABLE assets (
                id                   INTEGER PRIMARY KEY,
                path                 TEXT NOT NULL UNIQUE,
                asset_type           TEXT NOT NULL,
                object_version       TEXT,
                object_version_ue5   TEXT,
                is_unversioned       INTEGER,
                package_flags        INTEGER,
                engine_major         INTEGER,
                engine_minor         INTEGER,
                engine_patch         INTEGER,
                engine_changelist    INTEGER,
                engine_branch        TEXT,
                import_count         INTEGER NOT NULL DEFAULT 0,
                export_count         INTEGER NOT NULL DEFAULT 0,
                payload_toc_offset   INTEGER,
                data_resource_offset INTEGER,
                saved_hash           TEXT
            );
            CREATE INDEX idx_assets_path ON assets(path);
            CREATE INDEX idx_assets_type ON assets(asset_type);

            -- Per-asset engine subsystem versions
            CREATE TABLE custom_versions (
                id            INTEGER PRIMARY KEY,
                asset_id      INTEGER NOT NULL REFERENCES assets(id),
                guid          TEXT NOT NULL,
                friendly_name TEXT,
                version       INTEGER NOT NULL,
                UNIQUE(asset_id, guid)
            );
            CREATE INDEX idx_cv_asset ON custom_versions(asset_id);
            CREATE INDEX idx_cv_name ON custom_versions(friendly_name);

            -- Import table entries per file
            CREATE TABLE imports (
                id            INTEGER PRIMARY KEY,
                asset_id      INTEGER NOT NULL REFERENCES assets(id),
                import_index  INTEGER NOT NULL,
                object_name   TEXT NOT NULL,
                class_name    TEXT NOT NULL,
                class_package TEXT,
                outer_index   INTEGER,
                package_name  TEXT,
                is_optional   INTEGER DEFAULT 0,
                UNIQUE(asset_id, import_index)
            );
            CREATE INDEX idx_imports_asset ON imports(asset_id);
            CREATE INDEX idx_imports_object_name ON imports(object_name);
            CREATE INDEX idx_imports_class_name ON imports(class_name);

            -- Export table entries per file
            CREATE TABLE exports (
                id               INTEGER PRIMARY KEY,
                asset_id         INTEGER NOT NULL REFERENCES assets(id),
                export_index     INTEGER NOT NULL,
                object_name      TEXT NOT NULL,
                class_name       TEXT,
                super_name       TEXT,
                outer_index      INTEGER,
                object_flags     INTEGER,
                serial_size      INTEGER,
                is_asset         INTEGER DEFAULT 0,
                not_for_client   INTEGER DEFAULT 0,
                not_for_server   INTEGER DEFAULT 0,
                is_forced_export INTEGER DEFAULT 0,
                extras_size      INTEGER DEFAULT 0,
                UNIQUE(asset_id, export_index)
            );
            CREATE INDEX idx_exports_asset ON exports(asset_id);
            CREATE INDEX idx_exports_class_name ON exports(class_name);
            CREATE INDEX idx_exports_object_name ON exports(object_name);

            -- Serialization ordering dependencies per export
            CREATE TABLE export_dependencies (
                id           INTEGER PRIMARY KEY,
                export_id    INTEGER NOT NULL REFERENCES exports(id),
                dep_type     TEXT NOT NULL,
                target_index INTEGER NOT NULL
            );
            CREATE INDEX idx_expdeps_export ON export_dependencies(export_id);

            -- Recursive property tree per export
            CREATE TABLE properties (
                id            INTEGER PRIMARY KEY,
                export_id     INTEGER NOT NULL REFERENCES exports(id),
                parent_id     INTEGER REFERENCES properties(id),
                sort_order    INTEGER NOT NULL DEFAULT 0,
                name          TEXT NOT NULL,
                property_type TEXT NOT NULL,
                struct_type   TEXT,
                array_index   INTEGER DEFAULT 0,
                value_int     INTEGER,
                value_float   REAL,
                value_text    TEXT,
                value_ref     TEXT
            );
            CREATE INDEX idx_props_export ON properties(export_id);
            CREATE INDEX idx_props_parent ON properties(parent_id);
            CREATE INDEX idx_props_name ON properties(name);
            CREATE INDEX idx_props_name_type ON properties(name, property_type);
            CREATE INDEX idx_props_value_text ON properties(name, value_text) WHERE value_text IS NOT NULL;
            CREATE INDEX idx_props_value_int ON properties(name, value_int) WHERE value_int IS NOT NULL;
            CREATE INDEX idx_props_value_ref ON properties(value_ref) WHERE value_ref IS NOT NULL;

            -- Dependency graph edges
            CREATE TABLE edges (
                id        INTEGER PRIMARY KEY,
                source_id INTEGER NOT NULL REFERENCES assets(id),
                target_id INTEGER NOT NULL REFERENCES assets(id),
                ref_type  TEXT NOT NULL,
                UNIQUE(source_id, target_id, ref_type)
            );
            CREATE INDEX idx_edges_source ON edges(source_id);
            CREATE INDEX idx_edges_target ON edges(target_id);

            -- Localization strings
            CREATE TABLE gatherable_text (
                id               INTEGER PRIMARY KEY,
                asset_id         INTEGER NOT NULL REFERENCES assets(id),
                namespace        TEXT,
                source_string    TEXT,
                key_name         TEXT,
                site_description TEXT,
                is_editor_only   INTEGER DEFAULT 0
            );
            CREATE INDEX idx_gtext_asset ON gatherable_text(asset_id);
            CREATE INDEX idx_gtext_key ON gatherable_text(key_name) WHERE key_name IS NOT NULL;
            CREATE INDEX idx_gtext_source ON gatherable_text(source_string) WHERE source_string IS NOT NULL;

            -- Asset registry searchable names
            CREATE TABLE searchable_names (
                id           INTEGER PRIMARY KEY,
                asset_id     INTEGER NOT NULL REFERENCES assets(id),
                export_index INTEGER NOT NULL,
                name         TEXT NOT NULL
            );
            CREATE INDEX idx_snames_asset ON searchable_names(asset_id);
            CREATE INDEX idx_snames_name ON searchable_names(name);

            -- World composition tile data (maps only)
            CREATE TABLE world_tile_info (
                id                         INTEGER PRIMARY KEY,
                asset_id                   INTEGER NOT NULL REFERENCES assets(id),
                position_x                 INTEGER,
                position_y                 INTEGER,
                position_z                 INTEGER,
                abs_position_x             INTEGER,
                abs_position_y             INTEGER,
                abs_position_z             INTEGER,
                layer_name                 TEXT,
                streaming_distance         INTEGER,
                distance_streaming_enabled INTEGER,
                parent_tile_package        TEXT,
                z_order                    INTEGER,
                hide_in_tile_view          INTEGER DEFAULT 0,
                lod_list_json              TEXT
            );
            CREATE INDEX idx_wti_asset ON world_tile_info(asset_id);

            -- Scan-level key-value metadata
            CREATE TABLE meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )";

        cmd.ExecuteNonQuery();
    }

    // =================================================================
    // Prepared statement builders
    // =================================================================

    private SqliteCommand CreateInsertAssetCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO assets (
                path, asset_type, object_version, object_version_ue5,
                is_unversioned, package_flags,
                engine_major, engine_minor, engine_patch,
                engine_changelist, engine_branch,
                import_count, export_count,
                payload_toc_offset, data_resource_offset, saved_hash
            ) VALUES (
                $path, $assetType, $objectVersion, $objectVersionUE5,
                $isUnversioned, $packageFlags,
                $engineMajor, $engineMinor, $enginePatch,
                $engineChangelist, $engineBranch,
                $importCount, $exportCount,
                $payloadTocOffset, $dataResourceOffset, $savedHash
            )
            ON CONFLICT(path) DO UPDATE SET
                asset_type = excluded.asset_type,
                object_version = excluded.object_version,
                object_version_ue5 = excluded.object_version_ue5,
                is_unversioned = excluded.is_unversioned,
                package_flags = excluded.package_flags,
                engine_major = excluded.engine_major,
                engine_minor = excluded.engine_minor,
                engine_patch = excluded.engine_patch,
                engine_changelist = excluded.engine_changelist,
                engine_branch = excluded.engine_branch,
                import_count = excluded.import_count,
                export_count = excluded.export_count,
                payload_toc_offset = excluded.payload_toc_offset,
                data_resource_offset = excluded.data_resource_offset,
                saved_hash = excluded.saved_hash
            RETURNING id";
        cmd.Parameters.Add("$path", SqliteType.Text);
        cmd.Parameters.Add("$assetType", SqliteType.Text);
        cmd.Parameters.Add("$objectVersion", SqliteType.Text);
        cmd.Parameters.Add("$objectVersionUE5", SqliteType.Text);
        cmd.Parameters.Add("$isUnversioned", SqliteType.Integer);
        cmd.Parameters.Add("$packageFlags", SqliteType.Integer);
        cmd.Parameters.Add("$engineMajor", SqliteType.Integer);
        cmd.Parameters.Add("$engineMinor", SqliteType.Integer);
        cmd.Parameters.Add("$enginePatch", SqliteType.Integer);
        cmd.Parameters.Add("$engineChangelist", SqliteType.Integer);
        cmd.Parameters.Add("$engineBranch", SqliteType.Text);
        cmd.Parameters.Add("$importCount", SqliteType.Integer);
        cmd.Parameters.Add("$exportCount", SqliteType.Integer);
        cmd.Parameters.Add("$payloadTocOffset", SqliteType.Integer);
        cmd.Parameters.Add("$dataResourceOffset", SqliteType.Integer);
        cmd.Parameters.Add("$savedHash", SqliteType.Text);
        return cmd;
    }

    private SqliteCommand CreateInsertCustomVersionCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO custom_versions (asset_id, guid, friendly_name, version)
            VALUES ($assetId, $guid, $friendlyName, $version)";
        cmd.Parameters.Add("$assetId", SqliteType.Integer);
        cmd.Parameters.Add("$guid", SqliteType.Text);
        cmd.Parameters.Add("$friendlyName", SqliteType.Text);
        cmd.Parameters.Add("$version", SqliteType.Integer);
        return cmd;
    }

    private SqliteCommand CreateInsertImportCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO imports (
                asset_id, import_index, object_name, class_name,
                class_package, outer_index, package_name, is_optional
            ) VALUES (
                $assetId, $importIndex, $objectName, $className,
                $classPackage, $outerIndex, $packageName, $isOptional
            )";
        cmd.Parameters.Add("$assetId", SqliteType.Integer);
        cmd.Parameters.Add("$importIndex", SqliteType.Integer);
        cmd.Parameters.Add("$objectName", SqliteType.Text);
        cmd.Parameters.Add("$className", SqliteType.Text);
        cmd.Parameters.Add("$classPackage", SqliteType.Text);
        cmd.Parameters.Add("$outerIndex", SqliteType.Integer);
        cmd.Parameters.Add("$packageName", SqliteType.Text);
        cmd.Parameters.Add("$isOptional", SqliteType.Integer);
        return cmd;
    }

    private SqliteCommand CreateInsertExportCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO exports (
                asset_id, export_index, object_name, class_name,
                super_name, outer_index, object_flags, serial_size,
                is_asset, not_for_client, not_for_server,
                is_forced_export, extras_size
            ) VALUES (
                $assetId, $exportIndex, $objectName, $className,
                $superName, $outerIndex, $objectFlags, $serialSize,
                $isAsset, $notForClient, $notForServer,
                $isForcedExport, $extrasSize
            ) RETURNING id";
        cmd.Parameters.Add("$assetId", SqliteType.Integer);
        cmd.Parameters.Add("$exportIndex", SqliteType.Integer);
        cmd.Parameters.Add("$objectName", SqliteType.Text);
        cmd.Parameters.Add("$className", SqliteType.Text);
        cmd.Parameters.Add("$superName", SqliteType.Text);
        cmd.Parameters.Add("$outerIndex", SqliteType.Integer);
        cmd.Parameters.Add("$objectFlags", SqliteType.Integer);
        cmd.Parameters.Add("$serialSize", SqliteType.Integer);
        cmd.Parameters.Add("$isAsset", SqliteType.Integer);
        cmd.Parameters.Add("$notForClient", SqliteType.Integer);
        cmd.Parameters.Add("$notForServer", SqliteType.Integer);
        cmd.Parameters.Add("$isForcedExport", SqliteType.Integer);
        cmd.Parameters.Add("$extrasSize", SqliteType.Integer);
        return cmd;
    }

    private SqliteCommand CreateInsertExportDepCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO export_dependencies (export_id, dep_type, target_index)
            VALUES ($exportId, $depType, $targetIndex)";
        cmd.Parameters.Add("$exportId", SqliteType.Integer);
        cmd.Parameters.Add("$depType", SqliteType.Text);
        cmd.Parameters.Add("$targetIndex", SqliteType.Integer);
        return cmd;
    }

    private SqliteCommand CreateInsertPropertyCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO properties (
                export_id, parent_id, sort_order,
                name, property_type, struct_type, array_index,
                value_int, value_float, value_text, value_ref
            ) VALUES (
                $exportId, $parentId, $sortOrder,
                $name, $propertyType, $structType, $arrayIndex,
                $valueInt, $valueFloat, $valueText, $valueRef
            ) RETURNING id";
        cmd.Parameters.Add("$exportId", SqliteType.Integer);
        cmd.Parameters.Add("$parentId", SqliteType.Integer);
        cmd.Parameters.Add("$sortOrder", SqliteType.Integer);
        cmd.Parameters.Add("$name", SqliteType.Text);
        cmd.Parameters.Add("$propertyType", SqliteType.Text);
        cmd.Parameters.Add("$structType", SqliteType.Text);
        cmd.Parameters.Add("$arrayIndex", SqliteType.Integer);
        cmd.Parameters.Add("$valueInt", SqliteType.Integer);
        cmd.Parameters.Add("$valueFloat", SqliteType.Real);
        cmd.Parameters.Add("$valueText", SqliteType.Text);
        cmd.Parameters.Add("$valueRef", SqliteType.Text);
        return cmd;
    }

    private SqliteCommand CreateInsertEdgeCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO edges (source_id, target_id, ref_type)
            VALUES ($source, $target, $refType)";
        cmd.Parameters.Add("$source", SqliteType.Integer);
        cmd.Parameters.Add("$target", SqliteType.Integer);
        cmd.Parameters.Add("$refType", SqliteType.Text);
        return cmd;
    }

    private SqliteCommand CreateInsertGatherableTextCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO gatherable_text (
                asset_id, namespace, source_string,
                key_name, site_description, is_editor_only
            ) VALUES (
                $assetId, $namespace, $sourceString,
                $keyName, $siteDescription, $isEditorOnly
            )";
        cmd.Parameters.Add("$assetId", SqliteType.Integer);
        cmd.Parameters.Add("$namespace", SqliteType.Text);
        cmd.Parameters.Add("$sourceString", SqliteType.Text);
        cmd.Parameters.Add("$keyName", SqliteType.Text);
        cmd.Parameters.Add("$siteDescription", SqliteType.Text);
        cmd.Parameters.Add("$isEditorOnly", SqliteType.Integer);
        return cmd;
    }

    private SqliteCommand CreateInsertSearchableNameCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO searchable_names (asset_id, export_index, name)
            VALUES ($assetId, $exportIndex, $name)";
        cmd.Parameters.Add("$assetId", SqliteType.Integer);
        cmd.Parameters.Add("$exportIndex", SqliteType.Integer);
        cmd.Parameters.Add("$name", SqliteType.Text);
        return cmd;
    }

    private SqliteCommand CreateInsertWorldTileInfoCommand(SqliteTransaction transaction)
    {
        var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO world_tile_info (
                asset_id, position_x, position_y, position_z,
                abs_position_x, abs_position_y, abs_position_z,
                layer_name, streaming_distance, distance_streaming_enabled,
                parent_tile_package, z_order, hide_in_tile_view, lod_list_json
            ) VALUES (
                $assetId, $posX, $posY, $posZ,
                $absPosX, $absPosY, $absPosZ,
                $layerName, $streamingDistance, $distStreamEnabled,
                $parentTilePackage, $zOrder, $hideInTileView, $lodListJson
            )";
        cmd.Parameters.Add("$assetId", SqliteType.Integer);
        cmd.Parameters.Add("$posX", SqliteType.Integer);
        cmd.Parameters.Add("$posY", SqliteType.Integer);
        cmd.Parameters.Add("$posZ", SqliteType.Integer);
        cmd.Parameters.Add("$absPosX", SqliteType.Integer);
        cmd.Parameters.Add("$absPosY", SqliteType.Integer);
        cmd.Parameters.Add("$absPosZ", SqliteType.Integer);
        cmd.Parameters.Add("$layerName", SqliteType.Text);
        cmd.Parameters.Add("$streamingDistance", SqliteType.Integer);
        cmd.Parameters.Add("$distStreamEnabled", SqliteType.Integer);
        cmd.Parameters.Add("$parentTilePackage", SqliteType.Text);
        cmd.Parameters.Add("$zOrder", SqliteType.Integer);
        cmd.Parameters.Add("$hideInTileView", SqliteType.Integer);
        cmd.Parameters.Add("$lodListJson", SqliteType.Text);
        return cmd;
    }

    // =================================================================
    // Path helpers
    // =================================================================

    private static string GetDatabasePath(string projectPath)
    {
        var projectRoot = projectPath;
        var dirName = Path.GetFileName(projectPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(dirName, "UE_data", StringComparison.OrdinalIgnoreCase))
        {
            projectRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
        }

        return Path.Combine(projectRoot, "usr", "dependencies.db");
    }
}
