// Pumas Model Library - pumas-core UniFFI integration
//
// Wraps pumas-core via UniFFI-generated C# bindings (P/Invoke).
// Currently a stub returning empty results; will be connected
// once uniffi-bindgen-cs --library mode generates FfiPumasApi bindings.
//
// The native symbols are present in libpumas_uniffi.so and verified
// via nm. Awaiting tooling fix for auto-generated wrapper class.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent;

/// <summary>
/// Implementation of <see cref="IModelLibrary"/> backed by pumas-core via UniFFI.
/// </summary>
public sealed class PumasModelLibrary : IModelLibrary, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly string _launcherRoot;
    private bool _initialized;
    private bool _disposed;

    // TODO: Replace with generated FfiPumasApi handle once bindings are available.
    // The native library (libpumas_uniffi.so) exports all required symbols:
    //   uniffi_pumas_uniffi_fn_constructor_ffipumasapi_new
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_list_models
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_search_models
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_search_hf_models
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_start_hf_download
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_get_hf_download_progress
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_cancel_hf_download
    //   uniffi_pumas_uniffi_fn_method_ffipumasapi_is_online

    public PumasModelLibrary(string launcherRoot, IAppLogger logger)
    {
        _launcherRoot = launcherRoot ?? throw new ArgumentNullException(nameof(launcherRoot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsAvailable => _initialized;

    /// <summary>
    /// Initializes the connection to pumas-core.
    /// Must be called before any other operations.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.Info("Initializing PumasModelLibrary with root: {Root}", _launcherRoot);

        // TODO: Call FfiPumasApi.New(_launcherRoot) when bindings are available.
        // For now, mark as initialized to allow the rest of the system to work.
        await Task.CompletedTask;
        _initialized = true;

        _logger.Info("PumasModelLibrary initialized (stub mode - awaiting UniFFI bindings)");
    }

    /// <inheritdoc />
    public Task<List<ModelRecord>> ListModelsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(ListModelsAsync));
        return Task.FromResult(new List<ModelRecord>());
    }

    /// <inheritdoc />
    public Task<ModelRecord?> GetModelAsync(string modelId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(GetModelAsync));
        return Task.FromResult<ModelRecord?>(null);
    }

    /// <inheritdoc />
    public Task<ModelSearchResult> SearchModelsAsync(
        string query, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(SearchModelsAsync));
        return Task.FromResult(new ModelSearchResult(
            new List<ModelRecord>(),
            TotalCount: 0,
            QueryTimeMs: 0,
            Query: query
        ));
    }

    /// <inheritdoc />
    public Task<List<HuggingFaceModel>> SearchHuggingFaceAsync(
        string query, string? kind = null, int limit = 10, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(SearchHuggingFaceAsync));
        return Task.FromResult(new List<HuggingFaceModel>());
    }

    /// <inheritdoc />
    public Task<string> StartDownloadAsync(DownloadRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(StartDownloadAsync));
        throw new NotSupportedException(
            "Model downloads not available - pumas-core UniFFI bindings not yet connected.");
    }

    /// <inheritdoc />
    public Task<DownloadProgress?> GetDownloadProgressAsync(string downloadId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(GetDownloadProgressAsync));
        return Task.FromResult<DownloadProgress?>(null);
    }

    /// <inheritdoc />
    public Task<bool> CancelDownloadAsync(string downloadId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LogStubWarning(nameof(CancelDownloadAsync));
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public bool IsOnline()
    {
        // Default to true until pumas-core connection is established.
        return true;
    }

    private void LogStubWarning(string method)
    {
        _logger.Debug(
            "PumasModelLibrary.{Method} called in stub mode - returning empty result", method);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;
        // TODO: Dispose FfiPumasApi handle when bindings are available.
    }
}
