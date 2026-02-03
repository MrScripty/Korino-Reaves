// Model Library Interface
//
// Abstraction over pumas-core for model management operations.
// Currently implemented as a stub; will use UniFFI bindings
// when pumas-core C# binding generation is fully operational.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UAssetViewer.Agent;

/// <summary>
/// Model record from the local library.
/// Maps to pumas-core FfiModelRecord.
/// </summary>
public record ModelRecord(
    string Id,
    string Path,
    string CleanedName,
    string OfficialName,
    string ModelType,
    List<string> Tags,
    string HashesJson,
    string MetadataJson,
    string UpdatedAt
);

/// <summary>
/// Search result from the model library.
/// Maps to pumas-core FfiSearchResult.
/// </summary>
public record ModelSearchResult(
    List<ModelRecord> Models,
    long TotalCount,
    double QueryTimeMs,
    string Query
);

/// <summary>
/// A model available on HuggingFace.
/// Maps to pumas-core FfiHuggingFaceModel.
/// </summary>
public record HuggingFaceModel(
    string RepoId,
    string Name,
    string Developer,
    string Kind,
    List<string> Formats,
    List<string> Quants,
    List<DownloadOption> DownloadOptions,
    string Url,
    string? ReleaseDate,
    long? Downloads,
    long? TotalSizeBytes,
    string? QuantSizesJson,
    List<string> CompatibleEngines
);

/// <summary>
/// A download variant for a model.
/// </summary>
public record DownloadOption(
    string Quant,
    long? SizeBytes
);

/// <summary>
/// Request to download a model from HuggingFace.
/// </summary>
public record DownloadRequest(
    string RepoId,
    string Family,
    string OfficialName,
    string? ModelType = null,
    string? Quant = null,
    string? Filename = null
);

/// <summary>
/// Progress of an active download.
/// </summary>
public record DownloadProgress(
    string DownloadId,
    string? RepoId,
    string Status,
    float? Progress,
    long? DownloadedBytes,
    long? TotalBytes,
    double? Speed,
    double? EtaSeconds,
    string? Error
);

/// <summary>
/// Abstraction over pumas-core model management.
/// Enables model search, download, and library operations.
/// </summary>
public interface IModelLibrary
{
    /// <summary>
    /// Whether the library is connected and operational.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Lists all models in the local library.
    /// </summary>
    Task<List<ModelRecord>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a model by its ID.
    /// </summary>
    Task<ModelRecord?> GetModelAsync(string modelId, CancellationToken ct = default);

    /// <summary>
    /// Searches local models using full-text search.
    /// </summary>
    Task<ModelSearchResult> SearchModelsAsync(string query, int limit = 20, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Searches HuggingFace for models.
    /// </summary>
    Task<List<HuggingFaceModel>> SearchHuggingFaceAsync(string query, string? kind = null, int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Starts downloading a model from HuggingFace.
    /// Returns a download ID for tracking progress.
    /// </summary>
    Task<string> StartDownloadAsync(DownloadRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets download progress for an active download.
    /// </summary>
    Task<DownloadProgress?> GetDownloadProgressAsync(string downloadId, CancellationToken ct = default);

    /// <summary>
    /// Cancels an active download.
    /// </summary>
    Task<bool> CancelDownloadAsync(string downloadId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the network is online.
    /// </summary>
    bool IsOnline();
}
