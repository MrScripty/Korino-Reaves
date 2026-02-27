// No-Op Model Library
//
// Fallback model library used when pumas-core cannot initialize.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UAssetViewer.Agent;

/// <summary>
/// Fallback <see cref="IModelLibrary"/> implementation that reports unavailable.
/// </summary>
public sealed class NoOpModelLibrary : IModelLibrary
{
    private readonly string _reason;

    public NoOpModelLibrary(string reason)
    {
        _reason = string.IsNullOrWhiteSpace(reason) ? "Model library unavailable." : reason;
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<List<ModelRecord>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult(new List<ModelRecord>());

    /// <inheritdoc />
    public Task<ModelRecord?> GetModelAsync(string modelId, CancellationToken ct = default)
        => Task.FromResult<ModelRecord?>(null);

    /// <inheritdoc />
    public Task<ModelSearchResult> SearchModelsAsync(string query, int limit = 20, int offset = 0, CancellationToken ct = default)
        => Task.FromResult(new ModelSearchResult(new List<ModelRecord>(), 0, 0, query));

    /// <inheritdoc />
    public Task<List<HuggingFaceModel>> SearchHuggingFaceAsync(string query, string? kind = null, int limit = 10, CancellationToken ct = default)
        => Task.FromException<List<HuggingFaceModel>>(BuildUnavailableException());

    /// <inheritdoc />
    public Task<string> StartDownloadAsync(DownloadRequest request, CancellationToken ct = default)
        => Task.FromException<string>(BuildUnavailableException());

    /// <inheritdoc />
    public Task<DownloadProgress?> GetDownloadProgressAsync(string downloadId, CancellationToken ct = default)
        => Task.FromResult<DownloadProgress?>(null);

    /// <inheritdoc />
    public Task<bool> CancelDownloadAsync(string downloadId, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <inheritdoc />
    public bool IsOnline()
        => false;

    private InvalidOperationException BuildUnavailableException()
        => new(_reason);
}
