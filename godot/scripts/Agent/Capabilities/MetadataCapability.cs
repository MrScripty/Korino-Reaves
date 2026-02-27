// Metadata Capability
//
// Enforces bounded metadata query inputs and delegates to dependency data access.

using System;
using System.Diagnostics;
using System.Threading;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability implementation for asset metadata queries.
/// </summary>
public sealed class MetadataCapability : IMetadataCapability
{
    private const int DefaultRowLimit = 200;

    private readonly IProjectPathProvider _projectPathProvider;
    private readonly IDependencyDataAccess _dataAccess;
    private readonly IAppLogger _logger;
    private readonly AgentExecutionPolicy _policy;

    public MetadataCapability(
        IProjectPathProvider projectPathProvider,
        IDependencyDataAccess dataAccess,
        IAppLogger logger,
        AgentExecutionPolicy? policy = null)
    {
        _projectPathProvider = projectPathProvider ?? throw new ArgumentNullException(nameof(projectPathProvider));
        _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = policy ?? AgentExecutionPolicy.ReadOnlyDefault;
    }

    /// <inheritdoc />
    public AssetMetadataSnapshot? GetAssetMetadata(
        string assetPath,
        int rowLimit = DefaultRowLimit,
        CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = _projectPathProvider.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(assetPath))
        {
            LogTelemetry("getAssetMetadata", started.ElapsedMilliseconds, rowLimit, boundedRowLimit: 0, resultCount: 0);
            return null;
        }

        var boundedLimit = _policy.ClampMetadataRowLimit(rowLimit, DefaultRowLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = _dataAccess.GetAssetMetadata(projectPath, assetPath, boundedLimit, ct);
            ct.ThrowIfCancellationRequested();

            var rowCount = result == null
                ? 0
                : result.Imports.Length + result.Exports.Length + result.Properties.Length + result.Edges.Length;

            LogTelemetry("getAssetMetadata", started.ElapsedMilliseconds, rowLimit, boundedLimit, rowCount);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getAssetMetadata", started.ElapsedMilliseconds, rowLimit, boundedLimit, cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Metadata query failed for asset path: {Path}", assetPath);
            LogTelemetry(
                "getAssetMetadata",
                started.ElapsedMilliseconds,
                rowLimit,
                boundedLimit,
                resultCount: 0,
                cancelled: false,
                error: ex.GetType().Name);
            return null;
        }
    }

    private void LogTelemetry(
        string operation,
        long durationMs,
        int requestedRowLimit,
        int? boundedRowLimit = null,
        int? resultCount = null,
        bool cancelled = false,
        string? error = null)
    {
        var safeBoundedRowLimit = boundedRowLimit ?? -1;
        var safeResultCount = resultCount ?? -1;
        var safeError = error ?? string.Empty;

        _logger.Info(
            "Agent capability telemetry: capability={Capability} operation={Operation} durationMs={DurationMs} requestedLimit={RequestedLimit} boundedLimit={BoundedLimit} resultCount={ResultCount} cancelled={Cancelled} error={Error}",
            nameof(MetadataCapability),
            operation,
            durationMs,
            requestedRowLimit,
            safeBoundedRowLimit,
            safeResultCount,
            cancelled,
            safeError);
    }
}
