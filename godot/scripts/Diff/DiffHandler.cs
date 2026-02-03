// Diff Handler - IPC Handler for Diff Operations
//
// Handles diff-related IPC messages from the frontend.
// Routes requests to the appropriate diff engine components.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetAPI;
using UAssetViewer.Assets;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Diff;

/// <summary>
/// IPC handler for diff operations.
/// </summary>
public sealed class DiffHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;
    private readonly IDiffEngine _diffEngine;
    private readonly IConflictDetector _conflictDetector;
    private readonly IPatchGenerator _patchGenerator;
    private readonly IPatchApplier _patchApplier;
    private readonly AssetLoader _assetLoader;

    // Cached results for multi-step operations
    private DiffResult? _lastDiffResult;
    private ThreeWayDiffResult? _lastThreeWayResult;
    private UAsset? _baseAsset;
    private UAsset? _targetAsset;
    private UAsset? _moddedAsset;

    public string MessageType => MessageTypes.Diff;

    public DiffHandler(IAppLogger logger, AssetManager assetManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));

        _diffEngine = new DiffEngine(logger);
        _conflictDetector = new ConflictDetector(logger, _diffEngine);
        _patchGenerator = new PatchGenerator(logger);
        _patchApplier = new PatchApplier(logger);
        _assetLoader = new AssetLoader(logger);
    }

    public bool CanHandle(string action)
    {
        return action is "compare" or "threeWayCompare" or "applySafe" or
               "resolveConflict" or "clear" or "generatePatches" or
               "applyPatches" or "navigateTo";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("DiffHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "compare" => HandleCompare(message),
            "threeWayCompare" => HandleThreeWayCompare(message),
            "applySafe" => HandleApplySafe(message),
            "resolveConflict" => HandleResolveConflict(message),
            "clear" => HandleClear(message),
            "generatePatches" => HandleGeneratePatches(message),
            "applyPatches" => HandleApplyPatches(message),
            "navigateTo" => HandleNavigateTo(message),
            _ => Task.FromResult<IpcMessage?>(null)
        };
    }

    private async Task<IpcMessage?> HandleCompare(IpcMessage message)
    {
        try
        {
            var payload = ParsePayload<CompareRequest>(message.Payload);
            if (payload == null)
            {
                return CreateErrorResponse(message, "Invalid compare request");
            }

            _logger.Info("Comparing assets: {Base} vs {Target}", payload.BasePath, payload.TargetPath);

            // Send loading state
            SendLoading(true);

            // Load both assets
            _baseAsset = await _assetLoader.LoadAsync(payload.BasePath, null);
            _targetAsset = await _assetLoader.LoadAsync(payload.TargetPath, null);

            // Compute diff
            _lastDiffResult = _diffEngine.ComputeDiff(_baseAsset, _targetAsset);
            _lastThreeWayResult = null;

            SendLoading(false);

            return new IpcMessage(
                MessageTypes.Diff,
                "result",
                _lastDiffResult,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to compare assets");
            SendLoading(false);
            return CreateErrorResponse(message, ex.Message);
        }
    }

    private async Task<IpcMessage?> HandleThreeWayCompare(IpcMessage message)
    {
        try
        {
            var payload = ParsePayload<ThreeWayCompareRequest>(message.Payload);
            if (payload == null)
            {
                return CreateErrorResponse(message, "Invalid three-way compare request");
            }

            _logger.Info("Three-way compare: original={Original}, updated={Updated}, modded={Modded}",
                payload.OriginalPath, payload.UpdatedPath, payload.ModdedPath);

            SendLoading(true);

            // Load all three assets
            var original = await _assetLoader.LoadAsync(payload.OriginalPath, null);
            var updated = await _assetLoader.LoadAsync(payload.UpdatedPath, null);
            _moddedAsset = await _assetLoader.LoadAsync(payload.ModdedPath, null);

            // Store for later operations
            _baseAsset = original;
            _targetAsset = updated;

            // Perform three-way diff
            _lastThreeWayResult = _conflictDetector.PerformThreeWayDiff(original, updated, _moddedAsset);
            _lastDiffResult = null;

            SendLoading(false);

            return new IpcMessage(
                MessageTypes.Diff,
                "threeWayResult",
                _lastThreeWayResult,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform three-way compare");
            SendLoading(false);
            return CreateErrorResponse(message, ex.Message);
        }
    }

    private Task<IpcMessage?> HandleApplySafe(IpcMessage message)
    {
        try
        {
            if (_lastThreeWayResult == null || _targetAsset == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "No three-way diff result available"));
            }

            _logger.Info("Applying safe changes");

            var result = _patchApplier.ApplySafeChanges(_targetAsset, _lastThreeWayResult);

            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Diff,
                "safeApplied",
                result,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply safe changes");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleResolveConflict(IpcMessage message)
    {
        try
        {
            var payload = ParsePayload<ResolveConflictRequest>(message.Payload);
            if (payload == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "Invalid resolve conflict request"));
            }

            if (_lastThreeWayResult == null || _targetAsset == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "No three-way diff result available"));
            }

            _logger.Info("Resolving conflict at {Path} with resolution: {Resolution}",
                string.Join("/", payload.Path), payload.Resolution);

            // Find the conflict
            var conflict = FindConflict(payload.Path);
            if (conflict == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "Conflict not found"));
            }

            if (_patchApplier.ResolveConflict(
                _targetAsset, conflict, payload.Resolution, payload.CustomValue, out var error))
            {
                return Task.FromResult<IpcMessage?>(new IpcMessage(
                    MessageTypes.Diff,
                    "conflictResolved",
                    new { path = payload.Path, resolution = payload.Resolution },
                    message.Id,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ));
            }

            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, error ?? "Failed to resolve conflict"));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to resolve conflict");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleClear(IpcMessage message)
    {
        _logger.Info("Clearing diff state");

        _lastDiffResult = null;
        _lastThreeWayResult = null;
        _baseAsset = null;
        _targetAsset = null;
        _moddedAsset = null;

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Diff,
            "clear",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGeneratePatches(IpcMessage message)
    {
        try
        {
            if (_lastThreeWayResult == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "No three-way diff result available"));
            }

            _logger.Info("Generating patches from three-way diff");

            var patchSet = _patchGenerator.GeneratePatchesFromThreeWay(_lastThreeWayResult);

            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Diff,
                "patchesGenerated",
                patchSet,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate patches");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleApplyPatches(IpcMessage message)
    {
        try
        {
            var payload = ParsePayload<ApplyPatchesRequest>(message.Payload);
            if (payload == null || _targetAsset == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "Invalid apply patches request or no target asset"));
            }

            _logger.Info("Applying patches");

            var result = _patchApplier.ApplyPatches(_targetAsset, payload.PatchSet);

            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Diff,
                "patchesApplied",
                result,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply patches");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleNavigateTo(IpcMessage message)
    {
        try
        {
            var payload = ParsePayload<NavigateToRequest>(message.Payload);
            if (payload == null)
            {
                return Task.FromResult<IpcMessage?>(
                    CreateErrorResponse(message, "Invalid navigate request"));
            }

            _logger.Info("Navigate to path: {Path}", string.Join("/", payload.Path));

            // Tell the frontend to navigate to this path in the tree
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Selection,
                "navigateTo",
                new { path = payload.Path },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to navigate");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private DiffConflict? FindConflict(string[] path)
    {
        if (_lastThreeWayResult == null) return null;

        var pathKey = string.Join("/", path);
        return Array.Find(_lastThreeWayResult.Conflicts,
            c => string.Join("/", c.Path) == pathKey);
    }

    private void SendLoading(bool loading)
    {
        // This would send via the dispatcher, but we don't have direct access
        // In practice, the response flow handles this
        _logger.Debug("Loading state: {Loading}", loading);
    }

    private static T? ParsePayload<T>(object? payload) where T : class
    {
        if (payload == null) return null;
        if (payload is T typed) return typed;
        if (payload is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }
        return null;
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(ErrorCodes.DiffFailed, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    // Request DTOs
    private sealed record CompareRequest(string BasePath, string TargetPath);
    private sealed record ThreeWayCompareRequest(string OriginalPath, string UpdatedPath, string ModdedPath);
    private sealed record ResolveConflictRequest(string[] Path, string Resolution, object? CustomValue);
    private sealed record NavigateToRequest(string[] Path);
    private sealed record ApplyPatchesRequest(PatchSet PatchSet);
}
