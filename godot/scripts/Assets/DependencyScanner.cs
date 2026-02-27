// Dependency Scanner - Builds full asset metadata database from UAssetAPI
//
// Scans all .uasset/.umap files in a project, extracts package metadata,
// imports, exports, properties (recursive), dependency edges, custom
// versions, localization text, searchable names, and world tile info.
// Writes everything to a SQLite database. Runs after PAK extraction.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets;

/// <summary>
/// Progress update during scanning.
/// </summary>
public sealed record ScanProgress(
    int Current,
    int Total,
    string CurrentFile,
    string Phase
);

/// <summary>
/// Holds the result of parsing a single asset file so the writer thread
/// can insert into SQLite without re-accessing the UAsset on the parser thread.
/// </summary>
internal sealed class ParsedAssetResult
{
    public required string RelativePath { get; init; }
    public UAsset? Asset { get; init; }
    public bool ParseFailed { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Scans all assets in a project and builds a full metadata database in SQLite.
/// Uses concurrent parsing with a producer-consumer pattern for throughput.
/// </summary>
public sealed class DependencyScanner
{
    /// <summary>
    /// Number of CPU cores to reserve for system responsiveness during scanning.
    /// Matches PakHandler.ReservedCoresForSystem.
    /// </summary>
    private const int ReservedCoresForSystem = 1;

    /// <summary>
    /// Stack size for parser threads. UAssetAPI uses deep recursion when parsing
    /// complex assets; the default 1MB thread pool stack is insufficient.
    /// </summary>
    private const int ParserStackSize = 8 * 1024 * 1024;

    /// <summary>
    /// Capacity of the parse result channel. Bounds memory usage when parsers
    /// are faster than the SQLite writer.
    /// </summary>
    private const int ChannelCapacity = 256;

    private readonly IAppLogger _logger;

    public DependencyScanner(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Scans all .uasset/.umap files and builds the asset metadata database.
    /// Uses concurrent parsing (N threads) with a single-threaded SQLite writer.
    /// If a previous scan crashed (SIGSEGV from UAssetAPI), automatically
    /// detects the crashed file, adds it to a skip list, and retries.
    /// </summary>
    public void ScanProject(
        string projectPath,
        EngineVersion version,
        Action<ScanProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        _logger.Debug("Asset scan starting: {Path}, version: {Version}", projectPath, version);

        // --- Crash recovery ---
        var usrDir = ResolveUsrDirectory(projectPath);
        Directory.CreateDirectory(usrDir);
        var skipPath = Path.Combine(usrDir, ".scan-skiplist");
        var inflightPath = Path.Combine(usrDir, ".scan-inflight");

        var skipSet = LoadSkipSet(skipPath);
        RecoverCrashState(usrDir, skipSet, skipPath);

        if (skipSet.Count > 0)
        {
            _logger.Info("Skipping {Count} files that previously crashed UAssetAPI", skipSet.Count);
        }

        // Phase 1: Enumerate all asset files
        onProgress?.Invoke(new ScanProgress(0, 0, "", "enumerating"));

        var assetFiles = EnumerateAssetFiles(projectPath);
        if (assetFiles.Length == 0)
        {
            _logger.Warning("No asset files found in: {Path}", projectPath);
            return;
        }

        _logger.Debug("Found {Count} asset files to scan", assetFiles.Length);

        var gamePathLookup = BuildGamePathLookup(assetFiles, projectPath);

        // Phase 2: Open database and probe suspects
        using var db = new DependencyDatabase(_logger);
        db.CreateOrOpen(projectPath);

        var assetIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var probedCount = ProbeSuspects(usrDir, projectPath, version, skipSet, db, assetIds, gamePathLookup);

        // Load already-processed files (includes any just-probed suspects)
        var alreadyDone = db.GetExistingAssetPaths();
        if (alreadyDone.Count > 0)
        {
            _logger.Info("Resuming scan: {Count} files already processed", alreadyDone.Count);
        }

        // Phase 3: Filter to remaining work
        var filesToScan = assetFiles
            .Where(f => !skipSet.Contains(f) && !alreadyDone.Contains(f))
            .ToArray();

        if (filesToScan.Length == 0)
        {
            onProgress?.Invoke(new ScanProgress(assetFiles.Length, assetFiles.Length, "", "complete"));
            _logger.Info("Asset scan complete: all {Count} files already processed", assetFiles.Length);
            return;
        }

        // Phase 4: Concurrent parse + single-threaded write
        var maxParallelism = Math.Max(1, Environment.ProcessorCount - ReservedCoresForSystem);
        _logger.Info("Scanning {Count} files with {Parallelism} parser threads",
            filesToScan.Length, maxParallelism);

        onProgress?.Invoke(new ScanProgress(alreadyDone.Count, assetFiles.Length, "", "scanning"));

        var channel = Channel.CreateBounded<ParsedAssetResult>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        var inflight = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var fileQueue = new ConcurrentQueue<string>(filesToScan);

        // Start parser threads with 8MB stacks for UAssetAPI's deep recursion
        var parserThreads = new Thread[maxParallelism];
        for (int t = 0; t < maxParallelism; t++)
        {
            var thread = new Thread(
                () => ParserWorker(fileQueue, channel.Writer, projectPath, version,
                                   inflight, ct),
                ParserStackSize);
            thread.IsBackground = true;
            thread.Name = $"AssetParser-{t}";
            parserThreads[t] = thread;
            thread.Start();
        }

        // Completion watcher: signals channel complete when all parsers exit
        var completionThread = new Thread(() =>
        {
            foreach (var pt in parserThreads) pt.Join();
            channel.Writer.Complete();
        });
        completionThread.IsBackground = true;
        completionThread.Name = "ParserCompletion";
        completionThread.Start();

        // Writer loop runs on this thread (the calling 8MB-stack thread from DependencyHandler)
        WriterLoop(db, channel.Reader, gamePathLookup, inflight, inflightPath,
                   alreadyDone.Count, assetFiles.Length, version, onProgress, ct);

        completionThread.Join();

        // Cleanup
        if (File.Exists(inflightPath)) File.Delete(inflightPath);
        onProgress?.Invoke(new ScanProgress(assetFiles.Length, assetFiles.Length, "", "complete"));
    }

    // =================================================================
    // Asset-level metadata extraction
    // =================================================================

    private long InsertAssetMetadata(DependencyDatabase db, string relativePath, UAsset asset)
    {
        var ext = Path.GetExtension(relativePath).TrimStart('.').ToLowerInvariant();
        var assetType = ext is "umap" ? "umap" : "uasset";

        // Engine version
        int? engineMajor = null, engineMinor = null, enginePatch = null;
        int? engineChangelist = null;
        string? engineBranch = null;

        try
        {
            var ev = asset.RecordedEngineVersion;
            engineMajor = ev.Major;
            engineMinor = ev.Minor;
            enginePatch = ev.Patch;
            engineChangelist = (int)ev.Changelist;
            engineBranch = ev.Branch?.Value;
        }
        catch
        {
            // Some assets may not have engine version info
        }

        // UE5-specific fields — null for UE4
        long? payloadTocOffset = null;
        int? dataResourceOffset = null;
        string? savedHash = null;

        return db.InsertAsset(
            path: relativePath,
            assetType: assetType,
            objectVersion: asset.ObjectVersion.ToString(),
            objectVersionUE5: asset.ObjectVersionUE5.ToString(),
            isUnversioned: asset.IsUnversioned,
            packageFlags: (int)asset.PackageFlags,
            engineMajor: engineMajor,
            engineMinor: engineMinor,
            enginePatch: enginePatch,
            engineChangelist: engineChangelist,
            engineBranch: engineBranch,
            importCount: asset.Imports.Count,
            exportCount: asset.Exports.Count,
            payloadTocOffset: payloadTocOffset,
            dataResourceOffset: dataResourceOffset,
            savedHash: savedHash
        );
    }

    // =================================================================
    // Custom versions
    // =================================================================

    private void InsertCustomVersions(DependencyDatabase db, long assetId, UAsset asset)
    {
        if (asset.CustomVersionContainer == null) return;

        foreach (var cv in asset.CustomVersionContainer)
        {
            try
            {
                db.InsertCustomVersion(
                    assetId,
                    cv.Key.ToString(),
                    cv.FriendlyName,
                    cv.Version
                );
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to insert custom version: {Error}", ex.Message);
            }
        }
    }

    // =================================================================
    // Imports
    // =================================================================

    private void InsertImports(DependencyDatabase db, long assetId, UAsset asset)
    {
        for (int i = 0; i < asset.Imports.Count; i++)
        {
            var import = asset.Imports[i];

            db.InsertImport(
                assetId: assetId,
                importIndex: i,
                objectName: import.ObjectName?.Value?.Value ?? "None",
                className: import.ClassName?.Value?.Value ?? "None",
                classPackage: import.ClassPackage?.Value?.Value,
                outerIndex: import.OuterIndex?.Index,
                packageName: import.PackageName?.Value?.Value,
                isOptional: import.bImportOptional
            );
        }
    }

    // =================================================================
    // Exports + Properties + Export Dependencies
    // =================================================================

    private void InsertExports(DependencyDatabase db, long assetId, UAsset asset)
    {
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            var export = asset.Exports[i];

            string? className = ResolvePackageIndexName(asset, export.ClassIndex);
            string? superName = ResolvePackageIndexName(asset, export.SuperIndex);

            db.InsertExport(
                assetId: assetId,
                exportIndex: i,
                objectName: export.ObjectName?.Value?.Value ?? "None",
                className: className,
                superName: superName,
                outerIndex: export.OuterIndex?.Index,
                objectFlags: (int)export.ObjectFlags,
                serialSize: export.SerialSize,
                isAsset: export.bIsAsset,
                notForClient: export.bNotForClient,
                notForServer: export.bNotForServer,
                isForcedExport: export.bForcedExport,
                extrasSize: export.Extras?.Length ?? 0
            );
        }
    }

    /// <summary>
    /// Resolves an FPackageIndex to a human-readable name.
    /// </summary>
    private static string? ResolvePackageIndexName(UAsset asset, FPackageIndex? index)
    {
        if (index == null || index.IsNull()) return null;

        try
        {
            if (index.IsImport())
            {
                var import = index.ToImport(asset);
                return import?.ObjectName?.Value?.Value;
            }
            if (index.IsExport())
            {
                var export = index.ToExport(asset);
                return export?.ObjectName?.Value?.Value;
            }
        }
        catch
        {
            // Invalid index
        }

        return null;
    }

    // =================================================================
    // Dependency edges
    // =================================================================

    private int InsertDependencyEdges(
        DependencyDatabase db, long assetId, string relativePath,
        UAsset asset, Dictionary<string, string> gamePathLookup,
        Dictionary<string, long> assetIds,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        int edgeCount = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var import in asset.Imports)
        {
            var packagePath = ResolveImportPackagePath(asset, import);
            if (packagePath == null) continue;
            if (IsEngineReference(packagePath)) continue;

            if (!gamePathLookup.TryGetValue(packagePath, out var targetRelativePath))
            {
                var trimmed = packagePath.TrimStart('/');
                if (!gamePathLookup.TryGetValue(trimmed, out targetRelativePath))
                    continue;
            }

            var className = import.ClassName?.Value?.Value ?? "Unknown";
            var key = $"{targetRelativePath}|{className}";
            if (!seen.Add(key)) continue;

            if (!assetIds.TryGetValue(targetRelativePath, out var targetId))
            {
                var ext = Path.GetExtension(targetRelativePath).TrimStart('.').ToLowerInvariant();
                var assetType = ext is "umap" ? "umap" : "uasset";
                targetId = db.InsertAssetMinimal(targetRelativePath, assetType, transaction);
                assetIds[targetRelativePath] = targetId;
            }

            db.InsertEdge(assetId, targetId, className);
            edgeCount++;
        }

        return edgeCount;
    }

    // =================================================================
    // Gatherable text
    // =================================================================

    private void InsertGatherableText(DependencyDatabase db, long assetId, UAsset asset)
    {
        if (asset.GatherableTextData == null) return;

        foreach (var entry in asset.GatherableTextData)
        {
            var ns = entry.NamespaceName?.Value;
            var sourceString = entry.SourceData.SourceString?.Value;

            if (entry.SourceSiteContexts != null)
            {
                foreach (var ctx in entry.SourceSiteContexts)
                {
                    db.InsertGatherableText(
                        assetId,
                        ns,
                        sourceString,
                        ctx.KeyName?.Value,
                        ctx.SiteDescription?.Value,
                        ctx.IsEditorOnly
                    );
                }
            }
            else
            {
                // Entry with no site contexts — still record the text
                db.InsertGatherableText(assetId, ns, sourceString, null, null, false);
            }
        }
    }

    // =================================================================
    // Searchable names
    // =================================================================

    private void InsertSearchableNames(DependencyDatabase db, long assetId, UAsset asset)
    {
        if (asset.SearchableNames == null) return;

        foreach (var kvp in asset.SearchableNames)
        {
            var exportIndex = kvp.Key.Index;
            foreach (var name in kvp.Value)
            {
                var nameStr = name?.Value?.Value;
                if (!string.IsNullOrEmpty(nameStr))
                {
                    db.InsertSearchableName(assetId, exportIndex, nameStr);
                }
            }
        }
    }

    // =================================================================
    // World tile info
    // =================================================================

    private void InsertWorldTileInfo(DependencyDatabase db, long assetId, UAsset asset)
    {
        if (asset.WorldTileInfo == null) return;

        var wti = asset.WorldTileInfo;

        int? posX = null, posY = null, posZ = null;
        if (wti.Position is { Length: >= 3 })
        {
            posX = wti.Position[0];
            posY = wti.Position[1];
            posZ = wti.Position[2];
        }

        int? absPosX = null, absPosY = null, absPosZ = null;
        if (wti.AbsolutePosition is { Length: >= 3 })
        {
            absPosX = wti.AbsolutePosition[0];
            absPosY = wti.AbsolutePosition[1];
            absPosZ = wti.AbsolutePosition[2];
        }

        string? layerName = null;
        int? streamingDistance = null;
        bool distStreamEnabled = false;
        if (wti.Layer != null)
        {
            layerName = wti.Layer.Name?.Value;
            streamingDistance = wti.Layer.StreamingDistance;
            distStreamEnabled = wti.Layer.DistanceStreamingEnabled;
        }

        string? lodListJson = null;
        if (wti.LODList is { Length: > 0 })
        {
            var lodEntries = wti.LODList.Select(lod => new
            {
                relativeStreamingDistance = lod.RelativeStreamingDistance
            });
            lodListJson = JsonSerializer.Serialize(lodEntries);
        }

        db.InsertWorldTileInfo(
            assetId,
            posX, posY, posZ,
            absPosX, absPosY, absPosZ,
            layerName, streamingDistance, distStreamEnabled,
            wti.ParentTilePackageName?.Value,
            wti.ZOrder,
            wti.bHideInTileView,
            lodListJson
        );
    }

    // =================================================================
    // Crash recovery
    // =================================================================

    private static HashSet<string> LoadSkipSet(string skipPath)
    {
        var skipSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(skipPath))
        {
            foreach (var line in File.ReadAllLines(skipPath))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    skipSet.Add(line.Trim());
            }
        }
        return skipSet;
    }

    /// <summary>
    /// Handles crash recovery for both legacy single-file markers and
    /// concurrent inflight tracking. Moves inflight files to suspects
    /// for sequential probing.
    /// </summary>
    private void RecoverCrashState(
        string usrDir, HashSet<string> skipSet, string skipPath)
    {
        // Legacy single-file marker (from previous sequential scanner)
        var legacyMarkerPath = Path.Combine(usrDir, ".scan-marker");
        if (File.Exists(legacyMarkerPath))
        {
            var crashedFile = File.ReadAllText(legacyMarkerPath).Trim();
            if (!string.IsNullOrEmpty(crashedFile) && skipSet.Add(crashedFile))
            {
                _logger.Warning("Previous scan crashed on (legacy marker): {Path}", crashedFile);
                File.AppendAllText(skipPath, crashedFile + Environment.NewLine);
            }
            File.Delete(legacyMarkerPath);
        }

        // Concurrent inflight file → move to suspects for sequential probing
        var inflightPath = Path.Combine(usrDir, ".scan-inflight");
        var suspectsPath = Path.Combine(usrDir, ".scan-suspects");

        if (File.Exists(inflightPath))
        {
            var inflightFiles = File.ReadAllLines(inflightPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToArray();

            if (inflightFiles.Length > 0)
            {
                _logger.Warning("Previous scan crashed with {Count} files in-flight. Queuing for sequential probe.", inflightFiles.Length);
                File.AppendAllLines(suspectsPath, inflightFiles);
            }
            File.Delete(inflightPath);
        }
    }

    /// <summary>
    /// Probes suspect files sequentially with single-file markers to isolate
    /// the actual crasher. Files that parse successfully get inserted into the DB.
    /// Returns the number of successfully probed files.
    /// </summary>
    private int ProbeSuspects(
        string usrDir, string projectPath, EngineVersion version,
        HashSet<string> skipSet,
        DependencyDatabase db, Dictionary<string, long> assetIds,
        Dictionary<string, string> gamePathLookup)
    {
        var suspectsPath = Path.Combine(usrDir, ".scan-suspects");
        if (!File.Exists(suspectsPath)) return 0;

        var suspects = File.ReadAllLines(suspectsPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (suspects.Length == 0)
        {
            File.Delete(suspectsPath);
            return 0;
        }

        var alreadyDone = db.GetExistingAssetPaths();
        var markerPath = Path.Combine(usrDir, ".scan-marker");
        var probed = 0;

        _logger.Info("Probing {Count} suspect files sequentially...", suspects.Length);

        var transaction = db.BeginTransaction();
        db.PrepareInsertStatements(transaction);

        try
        {
            foreach (var relativePath in suspects)
            {
                if (skipSet.Contains(relativePath) || alreadyDone.Contains(relativePath))
                    continue;

                // Write single-file marker — if this file causes SIGSEGV,
                // next startup will promote it to the permanent skip list
                File.WriteAllText(markerPath, relativePath);

                try
                {
                    var fullPath = Path.Combine(projectPath, relativePath);
                    var asset = new UAsset(fullPath, false, version);

                    var assetId = InsertAssetMetadata(db, relativePath, asset);
                    assetIds[relativePath] = assetId;
                    InsertCustomVersions(db, assetId, asset);
                    InsertImports(db, assetId, asset);
                    InsertExports(db, assetId, asset);
                    InsertDependencyEdges(db, assetId, relativePath, asset, gamePathLookup, assetIds, transaction);
                    InsertGatherableText(db, assetId, asset);
                    InsertSearchableNames(db, assetId, asset);
                    InsertWorldTileInfo(db, assetId, asset);
                    probed++;
                }
                catch (Exception ex)
                {
                    _logger.Debug("Suspect file failed to parse: {Path}: {Error}", relativePath, ex.Message);
                }
            }

            db.DisposeInsertStatements();
            transaction.Commit();
        }
        finally
        {
            transaction.Dispose();
        }

        // Clean up markers
        if (File.Exists(markerPath)) File.Delete(markerPath);
        File.Delete(suspectsPath);

        if (probed > 0)
        {
            _logger.Info("Probed {Count} suspect files successfully", probed);
        }

        return probed;
    }

    /// <summary>
    /// Atomically writes all currently in-flight file paths to the marker file.
    /// </summary>
    private static void WriteInflightSnapshot(
        ConcurrentDictionary<string, byte> inflight, string inflightPath)
    {
        var paths = inflight.Keys.ToArray();
        if (paths.Length > 0)
        {
            var tmpPath = inflightPath + ".tmp";
            File.WriteAllLines(tmpPath, paths);
            File.Move(tmpPath, inflightPath, overwrite: true);
        }
        else if (File.Exists(inflightPath))
        {
            File.Delete(inflightPath);
        }
    }

    // =================================================================
    // Concurrent pipeline workers
    // =================================================================

    /// <summary>
    /// Worker method run on each parser thread. Dequeues files from the shared
    /// queue, parses them, and writes results to the channel.
    /// </summary>
    private void ParserWorker(
        ConcurrentQueue<string> fileQueue,
        ChannelWriter<ParsedAssetResult> writer,
        string projectPath,
        EngineVersion version,
        ConcurrentDictionary<string, byte> inflight,
        CancellationToken ct)
    {
        while (fileQueue.TryDequeue(out var relativePath))
        {
            if (ct.IsCancellationRequested) return;

            inflight.TryAdd(relativePath, 0);

            ParsedAssetResult result;
            try
            {
                var fullPath = Path.Combine(projectPath, relativePath);
                var asset = new UAsset(fullPath, false, version);
                result = new ParsedAssetResult
                {
                    RelativePath = relativePath,
                    Asset = asset,
                    ParseFailed = false
                };
            }
            catch (Exception ex)
            {
                result = new ParsedAssetResult
                {
                    RelativePath = relativePath,
                    Asset = null,
                    ParseFailed = true,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                // Runs for managed exceptions but NOT for SIGSEGV —
                // so a crashed file stays in the inflight dict
                inflight.TryRemove(relativePath, out _);
            }

            // Block if channel is full (backpressure from writer)
            writer.WriteAsync(result, ct).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Single-threaded writer loop. Drains the channel and writes parsed results
    /// to SQLite in batched transactions.
    /// </summary>
    private void WriterLoop(
        DependencyDatabase db,
        ChannelReader<ParsedAssetResult> reader,
        Dictionary<string, string> gamePathLookup,
        ConcurrentDictionary<string, byte> inflight,
        string inflightPath,
        int alreadyDoneCount,
        int totalFiles,
        EngineVersion version,
        Action<ScanProgress>? onProgress,
        CancellationToken ct)
    {
        var assetIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var totalEdges = 0;
        var processedCount = 0;
        var skippedCount = 0;
        const int batchSize = 100;
        var progressThrottle = Math.Max(1, totalFiles / 200);

        var transaction = db.BeginTransaction();
        db.PrepareInsertStatements(transaction);

        try
        {
            while (reader.WaitToReadAsync(ct).AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var result))
                {
                    if (result.ParseFailed)
                    {
                        _logger.Debug("Failed to scan {Path}: {Error}",
                            result.RelativePath, result.ErrorMessage ?? "Unknown error");
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        var asset = result.Asset!;
                        var assetId = InsertAssetMetadata(db, result.RelativePath, asset);
                        assetIds[result.RelativePath] = assetId;
                        InsertCustomVersions(db, assetId, asset);
                        InsertImports(db, assetId, asset);
                        InsertExports(db, assetId, asset);
                        var edgeCount = InsertDependencyEdges(db, assetId, result.RelativePath,
                            asset, gamePathLookup, assetIds, transaction);
                        totalEdges += edgeCount;
                        InsertGatherableText(db, assetId, asset);
                        InsertSearchableNames(db, assetId, asset);
                        InsertWorldTileInfo(db, assetId, asset);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("Failed to insert metadata for {Path}: {Error}",
                            result.RelativePath, ex.Message);
                    }

                    // Batch commit every N files
                    if (processedCount > 0 && processedCount % batchSize == 0)
                    {
                        transaction.Commit();
                        transaction.Dispose();

                        // Write inflight snapshot for crash recovery
                        WriteInflightSnapshot(inflight, inflightPath);

                        transaction = db.BeginTransaction();
                        db.UpdatePreparedTransactions(transaction);
                    }

                    // Rate-limited progress
                    var overallProgress = alreadyDoneCount + processedCount + skippedCount;
                    if (overallProgress % progressThrottle == 0)
                    {
                        onProgress?.Invoke(new ScanProgress(
                            overallProgress, totalFiles, result.RelativePath, "scanning"));
                    }
                }
            }

            // Final commit with metadata
            db.SetMeta("asset_count", (alreadyDoneCount + processedCount).ToString(), transaction);
            db.SetMeta("edge_count", totalEdges.ToString(), transaction);
            db.SetMeta("engine_version", version.ToString(), transaction);
            db.SetMeta("scanned_at", DateTime.UtcNow.ToString("o"), transaction);

            db.DisposeInsertStatements();
            transaction.Commit();
        }
        finally
        {
            transaction.Dispose();
        }

        if (skippedCount > 0)
        {
            _logger.Info("Skipped {Count} files due to parse errors", skippedCount);
        }

        _logger.Info("Asset scan complete: {Assets} assets ({New} new), {Edges} edges",
            alreadyDoneCount + processedCount, processedCount, totalEdges);
    }

    // =================================================================
    // File enumeration and path resolution
    // =================================================================

    private string[] EnumerateAssetFiles(string projectPath)
    {
        var extensions = new[] { "*.uasset", "*.umap" };
        var files = new List<string>();

        foreach (var ext in extensions)
        {
            try
            {
                var found = Directory.GetFiles(projectPath, ext, SearchOption.AllDirectories);
                foreach (var f in found)
                {
                    files.Add(Path.GetRelativePath(projectPath, f));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Error enumerating {Ext} files: {Error}", ext, ex.Message);
            }
        }

        return files.ToArray();
    }

    private Dictionary<string, string> BuildGamePathLookup(string[] assetFiles, string projectPath)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in assetFiles)
        {
            var withoutExt = Path.ChangeExtension(relativePath, null);
            var normalized = withoutExt.Replace(Path.DirectorySeparatorChar, '/');

            // Raw path entries (e.g. "/UE_data/AT/Content/ATContent/Foo")
            lookup.TryAdd("/" + normalized, relativePath);
            lookup.TryAdd(normalized, relativePath);

            // UE maps the Content/ directory to /Game/ in import references.
            // Find "Content/" in the path and create a /Game/ alias for
            // everything after it, so imports like /Game/ATContent/Foo resolve
            // to UE_data/AT/Content/ATContent/Foo.uasset.
            var contentIdx = normalized.IndexOf("Content/", StringComparison.OrdinalIgnoreCase);
            if (contentIdx >= 0)
            {
                var afterContent = normalized[(contentIdx + "Content/".Length)..];
                if (afterContent.Length > 0)
                {
                    lookup.TryAdd("/Game/" + afterContent, relativePath);
                    lookup.TryAdd("Game/" + afterContent, relativePath);
                }
            }
        }

        return lookup;
    }

    private static string? ResolveImportPackagePath(UAsset asset, Import import)
    {
        if (import.OuterIndex.IsNull() || import.OuterIndex.Index == 0)
        {
            var name = import.ObjectName?.Value?.Value;
            if (!string.IsNullOrEmpty(name) && name.Contains('/'))
                return name;
            return null;
        }

        var current = import.OuterIndex;
        string? packageName = null;

        for (int i = 0; i < 20; i++)
        {
            if (current == null || current.IsNull()) break;
            if (!current.IsImport()) break;

            var importIndex = -current.Index - 1;
            if (importIndex < 0 || importIndex >= asset.Imports.Count) break;

            var parentImport = asset.Imports[importIndex];
            var name = parentImport.ObjectName?.Value?.Value;
            if (string.IsNullOrEmpty(name)) break;

            packageName = name;
            current = parentImport.OuterIndex;
        }

        return packageName;
    }

    private static bool IsEngineReference(string packagePath)
    {
        return packagePath.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase)
            || packagePath.StartsWith("/Engine/", StringComparison.OrdinalIgnoreCase)
            || packagePath.StartsWith("/CoreUObject/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the usr/ directory for crash marker and skip list files.
    /// Mirrors the path logic in DependencyDatabase.GetDatabasePath.
    /// </summary>
    private static string ResolveUsrDirectory(string projectPath)
    {
        var projectRoot = projectPath;
        var dirName = Path.GetFileName(projectPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(dirName, "UE_data", StringComparison.OrdinalIgnoreCase))
        {
            projectRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
        }

        return Path.Combine(projectRoot, "usr");
    }
}
