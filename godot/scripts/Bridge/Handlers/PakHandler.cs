// PAK Handler - PAK Archive Operations
//
// Handles PAK-related IPC messages for extracting Unreal Engine archives.
// Uses PakManager for actual extraction operations.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using UAssetViewer.Assets;
using UAssetViewer.Assets.Compression;
using UAssetViewer.Bridge;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Request to extract a PAK file.
/// </summary>
public sealed record ExtractPakRequest(
    string PakPath,
    string ProjectName,
    string? GameVersion = null
);

/// <summary>
/// Handler for PAK-related IPC messages.
/// Extracts PAK archives to project directories.
/// </summary>
public sealed class PakHandler : IMessageHandler
{
    private static readonly Regex ValidProjectName = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Number of CPU cores to reserve for system responsiveness during extraction.
    /// </summary>
    private const int ReservedCoresForSystem = 1;

    /// <summary>
    /// Minimum number of files between progress updates to avoid IPC flooding.
    /// </summary>
    private const int MinProgressUpdateInterval = 50;

    /// <summary>
    /// Progress update frequency as percentage of total files (1% = every 100th of total).
    /// </summary>
    private const int ProgressUpdatePercentInterval = 100;

    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly PakManager _pakManager;
    private CancellationTokenSource? _extractionCts;
    private Task? _extractionTask;
    private Task? _postExtractionTask;
    private bool _isExtracting;

    public string MessageType => MessageTypes.Pak;

    public PakHandler(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _pakManager = new PakManager(logger);
    }

    public bool CanHandle(string action)
    {
        return action is "extract" or "import" or "cancel" or "validateName";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("PakHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "extract" or "import" => HandleExtract(message),
            "cancel" => HandleCancel(message),
            "validateName" => HandleValidateName(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private async Task<IpcMessage?> HandleExtract(IpcMessage message)
    {
        if (_isExtracting)
        {
            return CreateErrorResponse(message, "Import already in progress");
        }

        try
        {
            var request = ParsePayload<ExtractPakRequest>(message.Payload);
            if (request == null)
            {
                return CreateErrorResponse(message, "Invalid extract request");
            }

            // Validate project name
            if (string.IsNullOrWhiteSpace(request.ProjectName))
            {
                return CreateErrorResponse(message, "Project name is required");
            }

            if (!ValidProjectName.IsMatch(request.ProjectName))
            {
                return CreateErrorResponse(message, "Project name can only contain letters, numbers, underscores, and hyphens");
            }

            // Check pak file exists
            if (!File.Exists(request.PakPath))
            {
                return CreateErrorResponse(message, $"PAK file not found: {request.PakPath}");
            }

            // Ensure zlib-ng is available before attempting extraction
            if (!CompressionInitializerFactory.IsInitialized)
                CompressionInitializerFactory.EnsureInitialized(_logger);

            if (!CompressionInitializerFactory.IsInitialized)
            {
                return CreateErrorResponse(message,
                    "PAK decompression unavailable: native zlib-ng library not found. " +
                    "Run scripts/build-zlib-ng.sh or install zlib-ng for your platform.");
            }

            // Start extraction in background
            _extractionCts = new CancellationTokenSource();
            _isExtracting = true;

            _extractionTask = ExtractAsync(request, message.Id, _extractionCts.Token);
            _ = ObserveExtractionAsync(_extractionTask, request.PakPath);

            // Return immediate acknowledgment
            return new IpcMessage(
                MessageTypes.Pak,
                "importStarted",
                new { projectName = request.ProjectName },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start extraction");
            _isExtracting = false;
            return CreateErrorResponse(message, ex.Message);
        }
    }

    private async Task ExtractAsync(ExtractPakRequest request, string? requestId, CancellationToken ct)
    {
        try
        {
            _logger.Info("Starting extraction: {PakPath} -> {ProjectName}", request.PakPath, request.ProjectName);

            // Create output directory: ./projects/{projectName}/UE_data/
            var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
            // Go up one level from godot/ to project root
            projectRoot = Path.GetDirectoryName(projectRoot) ?? projectRoot;
            var outputDir = Path.Combine(projectRoot, "projects", request.ProjectName, "UE_data");

            _logger.Info("Output directory: {OutputDir}", outputDir);
            Directory.CreateDirectory(outputDir);

            // Open the PAK
            await _pakManager.OpenAsync(request.PakPath);

            // Get all files
            var files = _pakManager.ListFiles();
            var totalFiles = files.Length;

            _logger.Info("Extracting {Count} files...", totalFiles);

            // Send initial progress
            SendProgress(requestId, 0, totalFiles, "Starting extraction...");

            // Pre-create all directories upfront to avoid lock contention
            var directories = files
                .Select(f => Path.GetDirectoryName(Path.Combine(outputDir, f.Replace('/', Path.DirectorySeparatorChar))))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToList();

            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir!);
            }

            // Reserve cores for system responsiveness
            var maxParallelism = Math.Max(1, System.Environment.ProcessorCount - ReservedCoresForSystem);
            _logger.Info("Extracting with parallelism: {Parallelism}", maxParallelism);

            // Create provider pool for true parallel I/O (each provider has its own file handle)
            SendProgress(requestId, 0, totalFiles, "Initializing parallel extraction...");
            await _pakManager.CreateProviderPoolAsync(maxParallelism);

            var extractedCount = 0;
            var progressLock = new object();
            var lastProgressUpdate = 0;

            // Rate-limit progress updates to avoid IPC flooding
            var updateThreshold = Math.Max(MinProgressUpdateInterval, totalFiles / ProgressUpdatePercentInterval);

            // Index files so each parallel worker can use a different provider
            var indexedFiles = files.Select((file, index) => (file, index)).ToArray();

            await Parallel.ForEachAsync(indexedFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallelism,
                    CancellationToken = ct
                },
                async (item, token) =>
                {
                    var (file, index) = item;

                    try
                    {
                        // Get provider for this worker (round-robin assignment)
                        var provider = _pakManager.GetPooledProvider(index);
                        if (provider == null)
                        {
                            _logger.Warning("No provider available for file {File}", file);
                            return;
                        }

                        // Extract file using dedicated provider (true parallel I/O)
                        var data = await _pakManager.ExtractFileWithProviderAsync(provider, file);

                        // Write to disk
                        var outputPath = Path.Combine(outputDir, file.Replace('/', Path.DirectorySeparatorChar));
                        await File.WriteAllBytesAsync(outputPath, data, token);

                        // Thread-safe progress update
                        var count = Interlocked.Increment(ref extractedCount);

                        // Send streaming file extracted event (rate-limited)
                        if (count - lastProgressUpdate >= updateThreshold || count == totalFiles)
                        {
                            lock (progressLock)
                            {
                                if (count - lastProgressUpdate >= updateThreshold || count == totalFiles)
                                {
                                    lastProgressUpdate = count;
                                    var percent = (int)((count * 100.0) / totalFiles);
                                    SendProgress(requestId, count, totalFiles, $"Importing... ({percent}%)");

                                    // Send streaming file extracted event
                                    SendFileExtracted(requestId, file, count, totalFiles);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to extract file {File}: {Error}", file, ex.Message);
                        // Continue with other files - don't throw
                    }
                });

            _pakManager.Close();

            _logger.Info("Import complete: {Count} files extracted to {OutputDir}", extractedCount, outputDir);

            // Save game version to project config if specified
            if (!string.IsNullOrEmpty(request.GameVersion))
            {
                var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
                projectHandler?.SetGameVersionFromImport(outputDir, request.GameVersion);
                _logger.Info("Saved game version to project config: {Version}", request.GameVersion);
            }

            // Send completion
            SendComplete(requestId, outputDir, extractedCount);

            // Auto-open the project
            SendProjectOpened(request.ProjectName, outputDir, extractedCount);

            // Let the runtime and OS reclaim buffers from extraction before scanning
            await Task.Delay(500);

            _postExtractionTask = BuildDependencyGraphAsync(outputDir, request.GameVersion);
            _ = ObservePostExtractionAsync(_postExtractionTask, outputDir);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Extraction cancelled");
            SendCancelled(requestId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Extraction failed");
            SendError(requestId, ex.Message);
        }
        finally
        {
            _isExtracting = false;
            _extractionCts?.Dispose();
            _extractionCts = null;
        }
    }

    private async Task ObserveExtractionAsync(Task task, string pakPath)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unhandled extraction failure for {PakPath}", pakPath);
        }
        finally
        {
            if (ReferenceEquals(_extractionTask, task))
            {
                _extractionTask = null;
            }
        }
    }

    private async Task ObservePostExtractionAsync(Task task, string outputDir)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Unhandled post-extraction pipeline failure for {Path}: {Error}", outputDir, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_postExtractionTask, task))
            {
                _postExtractionTask = null;
            }
        }
    }

    private async Task BuildDependencyGraphAsync(string outputDir, string? gameVersion)
    {
        try
        {
            var depHandler = _dispatcher.GetHandler<DependencyHandler>();
            if (depHandler == null)
            {
                _logger.Warning("DependencyHandler not registered — skipping dependency scan");
                return;
            }

            // Resolve engine version from the import request
            var engineVersion = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_27;
            if (!string.IsNullOrEmpty(gameVersion) && gameVersion != "AUTO")
            {
                var versionName = gameVersion.Replace("GAME_", "VER_");
                if (Enum.TryParse<UAssetAPI.UnrealTypes.EngineVersion>(versionName, out var parsed))
                    engineVersion = parsed;
            }

            _logger.Info("Starting post-extraction dependency scan: {Path}", outputDir);
            await depHandler.RunScanAsync(outputDir, engineVersion);

            // Chain asset import after scan completes
            var importHandler = _dispatcher.GetHandler<ImportHandler>();
            if (importHandler != null)
                await importHandler.RunImportAsync(outputDir, gameVersion);
        }
        catch (Exception ex)
        {
            _logger.Warning("Post-extraction dependency scan failed (non-fatal): {Error}", ex.Message);
        }
    }

    private Task<IpcMessage?> HandleCancel(IpcMessage message)
    {
        if (_extractionCts != null && !_extractionCts.IsCancellationRequested)
        {
            _logger.Info("Cancelling extraction...");
            _extractionCts.Cancel();
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Pak,
            "cancelAcknowledged",
            null,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleValidateName(IpcMessage message)
    {
        var name = ParsePayloadString(message.Payload, "name") ?? "";

        var isValid = !string.IsNullOrWhiteSpace(name) && ValidProjectName.IsMatch(name);
        string? error = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Project name is required";
        }
        else if (!ValidProjectName.IsMatch(name))
        {
            error = "Only letters, numbers, underscores, and hyphens allowed";
        }
        else
        {
            // Check if directory already exists
            var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
            projectRoot = Path.GetDirectoryName(projectRoot) ?? projectRoot;
            var projectDir = Path.Combine(projectRoot, "projects", name);
            if (Directory.Exists(projectDir))
            {
                error = "A project with this name already exists";
                isValid = false;
            }
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Pak,
            "nameValidated",
            new { name, isValid, error },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private void SendProgress(string? requestId, int current, int total, string message)
    {
        _dispatcher.Send(new IpcMessage(
            MessageTypes.Pak,
            "progress",
            new { current, total, message, percent = total > 0 ? (current * 100) / total : 0 },
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private void SendComplete(string? requestId, string outputPath, int fileCount)
    {
        _dispatcher.Send(new IpcMessage(
            MessageTypes.Pak,
            "importComplete",
            new { outputPath, fileCount },
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private void SendFileExtracted(string? requestId, string filePath, int index, int total)
    {
        _dispatcher.Send(new IpcMessage(
            MessageTypes.Pak,
            "fileExtracted",
            new { filePath, index, total },
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private void SendProjectOpened(string projectName, string projectPath, int fileCount)
    {
        _dispatcher.Send(new IpcMessage(
            MessageTypes.Project,
            "opened",
            new { name = projectName, path = projectPath, fileCount },
            null,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private void SendCancelled(string? requestId)
    {
        _dispatcher.Send(new IpcMessage(
            MessageTypes.Pak,
            "importCancelled",
            null,
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private void SendError(string? requestId, string errorMessage)
    {
        _dispatcher.Send(new IpcMessage(
            MessageTypes.Pak,
            "importError",
            new { error = errorMessage },
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private static T? ParsePayload<T>(object? payload) where T : class
    {
        if (payload == null) return null;
        if (payload is T typed) return typed;
        if (payload is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
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
