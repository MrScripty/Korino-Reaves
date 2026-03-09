// Import Handler - IPC handler for asset pre-extraction cache
//
// Triggers the AssetImporter to extract all textures, meshes, and
// materials from the dependency database into Godot .res files.
// Follows the same pattern as DependencyHandler.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.UE4.Versions;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using UAssetViewer.Rendering;

namespace UAssetViewer.Bridge.Handlers;

public sealed class ImportHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly AssetImporter _importer;
    private CancellationTokenSource? _importCts;
    private Task? _currentImportTask;

    public string MessageType => MessageTypes.Import;

    public ImportHandler(IAppLogger logger, IpcDispatcher dispatcher, AssetImporter importer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
    }

    public bool CanHandle(string action)
    {
        return action is "start" or "cancel" or "getStatus";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        return message.Action switch
        {
            "start" => HandleStart(message),
            "cancel" => HandleCancel(message),
            "getStatus" => HandleGetStatus(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    // -----------------------------------------------------------------
    // Actions
    // -----------------------------------------------------------------

    private Task<IpcMessage?> HandleStart(IpcMessage message)
    {
        if (_importer.IsImporting || (_currentImportTask != null && !_currentImportTask.IsCompleted))
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Import already in progress"));
        }

        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler?.CurrentProject == null)
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));
        }

        var projectPath = projectHandler.CurrentProject.Path;
        var gameVersionStr = projectHandler.EffectiveGameVersion.ToString();

        StartImportInBackground(projectPath, gameVersionStr);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Import,
            "importStarted",
            new { projectPath },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleCancel(IpcMessage message)
    {
        if (_importCts != null && !_importCts.IsCancellationRequested)
        {
            _logger.Info("Cancelling asset import...");
            _importCts.Cancel();
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Import,
            "cancelAcknowledged",
            null,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetStatus(IpcMessage message)
    {
        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler?.CurrentProject == null)
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));
        }

        var projectPath = projectHandler.CurrentProject.Path;
        var gameVersionStr = projectHandler.EffectiveGameVersion.ToString();

        using var cache = new AssetCache(_logger);
        cache.Open(projectPath);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Import,
            "status",
            new
            {
                hasCache = cache.HasCache,
                isValid = cache.IsValid(gameVersionStr),
                isImporting = _importer.IsImporting,
                cacheDirectory = cache.CacheDirectory
            },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Public entry point for programmatic calls (e.g. from PakHandler)
    // -----------------------------------------------------------------

    /// <summary>
    /// Runs the full asset import pipeline. Can be called directly from
    /// other handlers (e.g. PakHandler after extraction + dependency scan).
    /// </summary>
    public async Task RunImportAsync(string projectPath, string? gameVersionStr)
    {
        var task = StartImportTask(projectPath, gameVersionStr);
        await AwaitOwnedImportAsync(task);
    }

    private void StartImportInBackground(string projectPath, string? gameVersionStr)
    {
        var task = StartImportTask(projectPath, gameVersionStr);
        _ = ObserveBackgroundImportAsync(task, projectPath);
    }

    private Task StartImportTask(string projectPath, string? gameVersionStr)
    {
        _importCts?.Cancel();
        _importCts?.Dispose();
        _importCts = new CancellationTokenSource();
        _currentImportTask = RunImportCoreAsync(projectPath, gameVersionStr, _importCts.Token);
        return _currentImportTask;
    }

    private async Task AwaitOwnedImportAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            ClearImportTask(task);
        }
    }

    private async Task ObserveBackgroundImportAsync(Task task, string projectPath)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unhandled background import failure for {ProjectPath}", projectPath);
        }
        finally
        {
            ClearImportTask(task);
        }
    }

    private void ClearImportTask(Task task)
    {
        if (!ReferenceEquals(_currentImportTask, task))
        {
            return;
        }

        _currentImportTask = null;
        _importCts?.Dispose();
        _importCts = null;
    }

    private async Task RunImportCoreAsync(string projectPath, string? gameVersionStr, CancellationToken ct)
    {

        try
        {
            // Resolve EGame version
            if (!Enum.TryParse<EGame>(gameVersionStr, out var eGameVersion))
            {
                eGameVersion = EGame.GAME_UE4_27;
                _logger.Warning("Could not parse EGame version '{Version}', defaulting to UE4_27",
                    gameVersionStr ?? "null");
            }

            // Open dependency database (read-only)
            using var db = new DependencyDatabase(_logger);
            db.Open(projectPath);

            if (!db.IsOpen)
            {
                _logger.Error("No dependency database found for project: {Path}. Run a scan first.", projectPath);
                _dispatcher.Send(new IpcMessage(
                    MessageTypes.Import,
                    "importError",
                    new { error = "No dependency database found. Run a dependency scan first." },
                    null,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ));
                return;
            }

            // Open or create cache
            using var cache = new AssetCache(_logger);
            cache.Open(projectPath);

            if (cache.IsValid(eGameVersion.ToString()))
            {
                _logger.Info("Asset cache is already valid for version {Version} — skipping import",
                    eGameVersion);
                _dispatcher.Send(new IpcMessage(
                    MessageTypes.Import,
                    "importComplete",
                    new { skipped = true, reason = "Cache already valid" },
                    null,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ));
                return;
            }

            // Create fresh cache
            cache.Create(projectPath, eGameVersion.ToString());

            // Run the import pipeline
            await _importer.ImportAllAsync(projectPath, eGameVersion, db, cache, onProgress: null, ct);

            _dispatcher.Send(new IpcMessage(
                MessageTypes.Import,
                "importComplete",
                new { skipped = false },
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Asset import cancelled");
            _dispatcher.Send(new IpcMessage(
                MessageTypes.Import,
                "importCancelled",
                null,
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Asset import failed");
            _dispatcher.Send(new IpcMessage(
                MessageTypes.Import,
                "importError",
                new { error = ex.Message },
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

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
