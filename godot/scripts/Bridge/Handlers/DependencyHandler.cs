// Dependency Handler - IPC handler for asset dependency graph queries
//
// Exposes the dependency database to the frontend via IPC.
// Supports scanning (building the graph) and querying (dependencies,
// dependents, related clusters, stats).

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SystemThread = System.Threading.Thread;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Assets;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

public sealed class DependencyHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly DependencyScanner _scanner;
    private CancellationTokenSource? _scanCts;
    private bool _isScanning;

    public string MessageType => MessageTypes.Dependency;

    public DependencyHandler(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scanner = new DependencyScanner(logger);
    }

    public bool CanHandle(string action)
    {
        return action is "scan" or "getDependencies" or "getDependents"
            or "getRelated" or "getStats" or "cancel"
            or "getAssetInfo" or "getImports" or "getExports"
            or "getProperties" or "searchByClass" or "searchProperties"
            or "getAssetTables";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        return message.Action switch
        {
            "scan" => HandleScan(message),
            "getDependencies" => HandleGetDependencies(message),
            "getDependents" => HandleGetDependents(message),
            "getRelated" => HandleGetRelated(message),
            "getStats" => HandleGetStats(message),
            "cancel" => HandleCancel(message),
            "getAssetInfo" => HandleGetAssetInfo(message),
            "getImports" => HandleGetImports(message),
            "getExports" => HandleGetExports(message),
            "getProperties" => HandleGetProperties(message),
            "searchByClass" => HandleSearchByClass(message),
            "searchProperties" => HandleSearchProperties(message),
            "getAssetTables" => HandleGetAssetTables(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    /// <summary>
    /// Triggers a full dependency scan of the current project.
    /// Can also be called internally after PAK extraction.
    /// </summary>
    public async Task<IpcMessage?> HandleScan(IpcMessage message)
    {
        if (_isScanning)
        {
            return CreateErrorResponse(message, "Dependency scan already in progress");
        }

        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler?.CurrentProject == null)
        {
            return CreateErrorResponse(message, "No project open");
        }

        var projectPath = projectHandler.CurrentProject.Path;
        var engineVersion = MapEGameToEngineVersion(projectHandler.EffectiveGameVersion.ToString())
            ?? EngineVersion.VER_UE4_27;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        _isScanning = true;

        // Run scan on dedicated thread with 8 MB stack to handle UAssetAPI's
        // deep recursion when parsing complex assets (thread pool default is 1 MB)
        var scanThread = new SystemThread(
            () => RunScanAsync(projectPath, engineVersion, message.Id, _scanCts.Token).GetAwaiter().GetResult(),
            maxStackSize: 8 * 1024 * 1024);
        scanThread.IsBackground = true;
        scanThread.Name = "DependencyScan";
        scanThread.Start();

        return new IpcMessage(
            MessageTypes.Dependency,
            "scanStarted",
            new { projectPath },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    /// <summary>
    /// Runs a dependency scan for the given project path and version.
    /// Called both from IPC (HandleScan) and from PakHandler after extraction.
    /// </summary>
    public async Task RunScanAsync(string projectPath, EngineVersion engineVersion,
        string? requestId = null, CancellationToken ct = default)
    {
        try
        {
            _isScanning = true;

            _scanner.ScanProject(projectPath, engineVersion, p =>
            {
                try
                {
                    _dispatcher.Send(new IpcMessage(
                        MessageTypes.Dependency,
                        "scanProgress",
                        new { current = p.Current, total = p.Total, currentFile = p.CurrentFile, phase = p.Phase },
                        requestId,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    ));
                }
                catch
                {
                    // Don't let IPC errors crash the scan
                }
            }, ct);

            // Read back stats from the newly created DB
            using var db = new DependencyDatabase(_logger);
            db.Open(projectPath);
            var stats = db.GetStats();

            _dispatcher.Send(new IpcMessage(
                MessageTypes.Dependency,
                "scanComplete",
                new
                {
                    assetCount = stats?.AssetCount ?? 0,
                    edgeCount = stats?.EdgeCount ?? 0,
                    scannedAt = stats?.ScannedAt.ToString("o"),
                },
                requestId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Dependency scan cancelled");
            _dispatcher.Send(new IpcMessage(
                MessageTypes.Dependency,
                "scanCancelled",
                null,
                requestId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Dependency scan failed");
            _dispatcher.Send(new IpcMessage(
                MessageTypes.Dependency,
                "scanError",
                new { error = ex.Message },
                requestId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        finally
        {
            _isScanning = false;
        }
    }

    private Task<IpcMessage?> HandleGetDependencies(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No dependency database found. Run a scan first."));

        var deps = db.GetDependencies(path);
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "dependencies",
            new { assetPath = path, dependencies = deps },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetDependents(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No dependency database found. Run a scan first."));

        var dependents = db.GetDependents(path);
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "dependents",
            new { assetPath = path, dependents },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetRelated(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var maxDepth = ParsePayloadInt(message.Payload, "maxDepth") ?? 3;

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No dependency database found. Run a scan first."));

        var related = db.GetRelatedCluster(path, maxDepth);
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "related",
            new { assetPath = path, maxDepth, related },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetStats(IpcMessage message)
    {
        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        if (!DependencyDatabase.Exists(projectPath))
        {
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Dependency,
                "stats",
                new { exists = false },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        var stats = db.GetStats();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "stats",
            new
            {
                exists = true,
                assetCount = stats?.AssetCount ?? 0,
                edgeCount = stats?.EdgeCount ?? 0,
                engineVersion = stats?.EngineVersion,
                scannedAt = stats?.ScannedAt.ToString("o"),
            },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleCancel(IpcMessage message)
    {
        if (_scanCts != null && !_scanCts.IsCancellationRequested)
        {
            _logger.Info("Cancelling dependency scan...");
            _scanCts.Cancel();
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "cancelAcknowledged",
            null,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Asset metadata queries
    // -----------------------------------------------------------------

    private Task<IpcMessage?> HandleGetAssetInfo(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var info = db.GetAssetInfo(path);
        if (info == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Asset not found in database"));

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "assetInfo",
            info,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetImports(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var imports = db.GetImports(path);
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "imports",
            new { assetPath = path, imports },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetExports(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var exports = db.GetExports(path);
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "exports",
            new { assetPath = path, exports },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetProperties(IpcMessage message)
    {
        var exportId = ParsePayloadLong(message.Payload, "exportId");
        if (exportId == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing exportId"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var properties = db.GetProperties(exportId.Value);
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "properties",
            new { exportId = exportId.Value, properties },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleSearchByClass(IpcMessage message)
    {
        var className = ParsePayloadString(message.Payload, "className");
        if (className == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing className"));

        var limit = ParsePayloadInt(message.Payload, "limit") ?? 100;

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var results = db.SearchByClassName(className, limit);
        var mapped = results.Select(r => new { assetPath = r.AssetPath, export = r.Export }).ToArray();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "searchByClassResults",
            new { className, results = mapped },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleSearchProperties(IpcMessage message)
    {
        var propertyName = ParsePayloadString(message.Payload, "propertyName");
        if (propertyName == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing propertyName"));

        var valueFilter = ParsePayloadString(message.Payload, "value");
        var limit = ParsePayloadInt(message.Payload, "limit") ?? 100;

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var results = db.SearchProperties(propertyName, valueFilter, limit);
        var mapped = results.Select(r => new
        {
            assetPath = r.AssetPath,
            exportName = r.ExportName,
            property = r.Property
        }).ToArray();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "searchPropertiesResults",
            new { propertyName, value = valueFilter, results = mapped },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Combined table query
    // -----------------------------------------------------------------

    private Task<IpcMessage?> HandleGetAssetTables(IpcMessage message)
    {
        var path = ParsePayloadString(message.Payload, "path");
        if (path == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing path"));

        var projectPath = GetProjectPath();
        if (projectPath == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        using var db = new DependencyDatabase(_logger);
        db.Open(projectPath);
        if (!db.IsOpen) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No asset database found. Run a scan first."));

        var info = db.GetAssetInfo(path);
        if (info == null) return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Asset not found in database"));

        var imports = db.GetImports(path);
        var exports = db.GetExports(path);
        var properties = db.GetAllProperties(path);
        var customVersions = db.GetCustomVersions(path);
        var edges = db.GetEdges(path);
        var gatherableText = db.GetGatherableText(path);
        var searchableNames = db.GetSearchableNames(path);
        var worldTileInfo = db.GetWorldTileInfo(path);
        var exportDependencies = db.GetExportDependencies(path);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Dependency,
            "assetTables",
            new
            {
                assetPath = path,
                assetInfo = info,
                imports,
                exports,
                properties,
                customVersions,
                edges,
                gatherableText,
                searchableNames,
                worldTileInfo,
                exportDependencies,
            },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private string? GetProjectPath()
    {
        return _dispatcher.GetHandler<ProjectHandler>()?.CurrentProject?.Path;
    }

    private static EngineVersion? MapEGameToEngineVersion(string eGameName)
    {
        var versionName = eGameName.Replace("GAME_", "VER_");
        if (Enum.TryParse<EngineVersion>(versionName, out var version))
            return version;
        return null;
    }

    private static string? ParsePayloadString(object? payload, string propertyName)
    {
        if (payload is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    private static int? ParsePayloadInt(object? payload, string propertyName)
    {
        if (payload is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.TryGetInt32(out var value))
                return value;
        }
        return null;
    }

    private static long? ParsePayloadLong(object? payload, string propertyName)
    {
        if (payload is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.TryGetInt64(out var value))
                return value;
        }
        return null;
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(ErrorCodes.InternalError, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }
}
