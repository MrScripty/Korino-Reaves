// Pumas Model Library - pumas-core UniFFI integration
//
// Wraps pumas-core via UniFFI-generated C# bindings (P/Invoke).
// Delegates all operations to FfiPumasApi from the generated bindings,
// mapping between FFI types (uniffi.pumas_uniffi) and domain types
// (UAssetViewer.Agent).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using FfiApi = uniffi.pumas_uniffi.FfiPumasApi;
using FfiRecord = uniffi.pumas_uniffi.FfiModelRecord;
using FfiSearch = uniffi.pumas_uniffi.FfiSearchResult;
using FfiHfModel = uniffi.pumas_uniffi.FfiHuggingFaceModel;
using FfiDownloadOption = uniffi.pumas_library.DownloadOption;
using FfiDownloadRequest = uniffi.pumas_library.DownloadRequest;
using FfiDownloadProgress = uniffi.pumas_library.ModelDownloadProgress;
using FfiException = uniffi.pumas_uniffi.FfiException;

namespace UAssetViewer.Agent;

/// <summary>
/// Implementation of <see cref="IModelLibrary"/> backed by pumas-core via UniFFI.
/// </summary>
public sealed class PumasModelLibrary : IModelLibrary, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly string _launcherRoot;
    private FfiApi? _api;
    private bool _disposed;

    public PumasModelLibrary(string launcherRoot, IAppLogger logger)
    {
        _launcherRoot = launcherRoot ?? throw new ArgumentNullException(nameof(launcherRoot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsAvailable => _api != null;

    /// <summary>
    /// Initializes the connection to pumas-core.
    /// Must be called before any other operations.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        _logger.Info("Initializing PumasModelLibrary with root: {Root}", _launcherRoot);

        try
        {
            _api = await FfiApi.WithOptions(_launcherRoot, autoCreateDirs: true, enableHf: true)
                .ConfigureAwait(false);
            _logger.Info("PumasModelLibrary initialized successfully");
        }
        catch (FfiException ex)
        {
            _logger.Error(ex, "Failed to initialize pumas-core: {Message}", ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ModelRecord>> ListModelsAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        var ffiModels = await _api!.ListModels().ConfigureAwait(false);
        return ffiModels.Select(MapModelRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<ModelRecord?> GetModelAsync(string modelId, CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        var ffiModel = await _api!.GetModel(modelId).ConfigureAwait(false);
        return ffiModel != null ? MapModelRecord(ffiModel) : null;
    }

    /// <inheritdoc />
    public async Task<ModelSearchResult> SearchModelsAsync(
        string query, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        var ffiResult = await _api!.SearchModels(query, (ulong)limit, (ulong)offset)
            .ConfigureAwait(false);
        return MapSearchResult(ffiResult);
    }

    /// <inheritdoc />
    public async Task<List<HuggingFaceModel>> SearchHuggingFaceAsync(
        string query, string? kind = null, int limit = 10, CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        var ffiModels = await _api!.SearchHfModels(query, kind, (ulong)limit)
            .ConfigureAwait(false);
        return ffiModels.Select(MapHuggingFaceModel).ToList();
    }

    /// <inheritdoc />
    public async Task<string> StartDownloadAsync(DownloadRequest request, CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        var ffiRequest = MapDownloadRequest(request);
        return await _api!.StartHfDownload(ffiRequest).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DownloadProgress?> GetDownloadProgressAsync(string downloadId, CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        var ffiProgress = await _api!.GetHfDownloadProgress(downloadId).ConfigureAwait(false);
        return ffiProgress != null ? MapDownloadProgress(ffiProgress) : null;
    }

    /// <inheritdoc />
    public async Task<bool> CancelDownloadAsync(string downloadId, CancellationToken ct = default)
    {
        EnsureInitialized();
        ct.ThrowIfCancellationRequested();

        return await _api!.CancelHfDownload(downloadId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsOnline()
    {
        if (_api == null) return false;
        return _api.IsOnline();
    }

    // =========================================================================
    // Type Mapping Helpers
    // =========================================================================

    private static ModelRecord MapModelRecord(FfiRecord r) => new(
        Id: r.id,
        Path: r.path,
        CleanedName: r.cleanedName,
        OfficialName: r.officialName,
        ModelType: r.modelType,
        Tags: r.tags,
        HashesJson: r.hashesJson,
        MetadataJson: r.metadataJson,
        UpdatedAt: r.updatedAt
    );

    private static ModelSearchResult MapSearchResult(FfiSearch r) => new(
        Models: r.models.Select(MapModelRecord).ToList(),
        TotalCount: (long)r.totalCount,
        QueryTimeMs: r.queryTimeMs,
        Query: r.query
    );

    private static HuggingFaceModel MapHuggingFaceModel(FfiHfModel m) => new(
        RepoId: m.repoId,
        Name: m.name,
        Developer: m.developer,
        Kind: m.kind,
        Formats: m.formats,
        Quants: m.quants,
        DownloadOptions: m.downloadOptions.Select(MapDownloadOption).ToList(),
        Url: m.url,
        ReleaseDate: m.releaseDate,
        Downloads: m.downloads.HasValue ? (long)m.downloads.Value : null,
        TotalSizeBytes: m.totalSizeBytes.HasValue ? (long)m.totalSizeBytes.Value : null,
        QuantSizesJson: m.quantSizesJson,
        CompatibleEngines: m.compatibleEngines
    );

    private static DownloadOption MapDownloadOption(FfiDownloadOption o) => new(
        Quant: o.quant,
        SizeBytes: o.sizeBytes.HasValue ? (long)o.sizeBytes.Value : null
    );

    private static FfiDownloadRequest MapDownloadRequest(DownloadRequest r) => new(
        repoId: r.RepoId,
        family: r.Family,
        officialName: r.OfficialName,
        modelType: r.ModelType,
        quant: r.Quant,
        filename: r.Filename
    );

    private static DownloadProgress MapDownloadProgress(FfiDownloadProgress p) => new(
        DownloadId: p.downloadId,
        RepoId: p.repoId,
        Status: p.status.ToString(),
        Progress: p.progress,
        DownloadedBytes: p.downloadedBytes.HasValue ? (long)p.downloadedBytes.Value : null,
        TotalBytes: p.totalBytes.HasValue ? (long)p.totalBytes.Value : null,
        Speed: p.speed,
        EtaSeconds: p.etaSeconds,
        Error: p.error
    );

    // =========================================================================
    // Internal Helpers
    // =========================================================================

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_api == null)
            throw new InvalidOperationException(
                "PumasModelLibrary has not been initialized. Call InitializeAsync() first.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _api?.Dispose();
        _api = null;
    }
}
