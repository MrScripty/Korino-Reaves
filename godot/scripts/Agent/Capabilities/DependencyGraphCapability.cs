// Dependency Graph Capability
//
// Enforces bounded graph traversal/query inputs and delegates to a data-access
// adapter that wraps the current metadata database implementation.

using System;
using System.Diagnostics;
using System.Threading;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability implementation for dependency graph operations.
/// </summary>
public sealed class DependencyGraphCapability : IDependencyGraphCapability
{
    private const int DefaultLimit = 100;
    private const int DefaultRelatedLimit = 200;
    private const int DefaultDepth = 3;

    private readonly IProjectPathProvider _projectPathProvider;
    private readonly IDependencyDataAccess _dataAccess;
    private readonly IAppLogger _logger;
    private readonly AgentExecutionPolicy _policy;

    public DependencyGraphCapability(
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
    public DependencyGraphStats GetStats(CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = _projectPathProvider.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            LogTelemetry("getStats", started.ElapsedMilliseconds, resultCount: 0);
            return new DependencyGraphStats(false);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var stats = _dataAccess.GetStats(projectPath, ct);
            ct.ThrowIfCancellationRequested();
            LogTelemetry("getStats", started.ElapsedMilliseconds, resultCount: stats.Exists ? 1 : 0);
            return stats;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getStats", started.ElapsedMilliseconds, cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Dependency graph stats query failed");
            LogTelemetry("getStats", started.ElapsedMilliseconds, error: ex.GetType().Name);
            return new DependencyGraphStats(false);
        }
    }

    /// <inheritdoc />
    public DependencyEdge[] GetDependencies(string assetPath, int limit = DefaultLimit, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(assetPath))
        {
            LogTelemetry("getDependencies", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: 0, resultCount: 0);
            return Array.Empty<DependencyEdge>();
        }

        var boundedLimit = _policy.ClampDependencyQueryLimit(limit, DefaultLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = _dataAccess.GetDependencies(projectPath, assetPath, boundedLimit, ct);
            ct.ThrowIfCancellationRequested();
            LogTelemetry("getDependencies", started.ElapsedMilliseconds, limit, boundedLimit, resultCount: result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getDependencies", started.ElapsedMilliseconds, limit, boundedLimit, cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            LogTelemetry("getDependencies", started.ElapsedMilliseconds, limit, boundedLimit, error: ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public DependencyEdge[] GetDependents(string assetPath, int limit = DefaultLimit, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(assetPath))
        {
            LogTelemetry("getDependents", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: 0, resultCount: 0);
            return Array.Empty<DependencyEdge>();
        }

        var boundedLimit = _policy.ClampDependencyQueryLimit(limit, DefaultLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = _dataAccess.GetDependents(projectPath, assetPath, boundedLimit, ct);
            ct.ThrowIfCancellationRequested();
            LogTelemetry("getDependents", started.ElapsedMilliseconds, limit, boundedLimit, resultCount: result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getDependents", started.ElapsedMilliseconds, limit, boundedLimit, cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            LogTelemetry("getDependents", started.ElapsedMilliseconds, limit, boundedLimit, error: ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public string[] GetRelated(
        string assetPath,
        int maxDepth = DefaultDepth,
        int limit = DefaultRelatedLimit,
        CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(assetPath))
        {
            LogTelemetry("getRelated", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: 0, requestedDepth: maxDepth, boundedDepth: 0, resultCount: 0);
            return Array.Empty<string>();
        }

        var boundedDepth = _policy.ClampDependencyTraversalDepth(maxDepth, DefaultDepth);
        var boundedLimit = _policy.ClampDependencyRelatedLimit(limit, DefaultRelatedLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = _dataAccess.GetRelated(projectPath, assetPath, boundedDepth, boundedLimit, ct);
            ct.ThrowIfCancellationRequested();
            LogTelemetry(
                "getRelated",
                started.ElapsedMilliseconds,
                limit,
                boundedLimit,
                maxDepth,
                boundedDepth,
                result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry(
                "getRelated",
                started.ElapsedMilliseconds,
                limit,
                boundedLimit,
                maxDepth,
                boundedDepth,
                cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            LogTelemetry(
                "getRelated",
                started.ElapsedMilliseconds,
                limit,
                boundedLimit,
                maxDepth,
                boundedDepth,
                error: ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public ClassSearchHit[] SearchByClass(string className, int limit = DefaultLimit, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(className))
        {
            LogTelemetry("searchByClass", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: 0, resultCount: 0);
            return Array.Empty<ClassSearchHit>();
        }

        var boundedLimit = _policy.ClampDependencyQueryLimit(limit, DefaultLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = _dataAccess.SearchByClass(projectPath, className, boundedLimit, ct);
            ct.ThrowIfCancellationRequested();
            LogTelemetry("searchByClass", started.ElapsedMilliseconds, limit, boundedLimit, resultCount: result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("searchByClass", started.ElapsedMilliseconds, limit, boundedLimit, cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            LogTelemetry("searchByClass", started.ElapsedMilliseconds, limit, boundedLimit, error: ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public PropertySearchHit[] SearchProperties(
        string propertyName,
        string? valueFilter = null,
        int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var projectPath = RequireProjectPath();
        if (projectPath == null || string.IsNullOrWhiteSpace(propertyName))
        {
            LogTelemetry("searchProperties", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: 0, resultCount: 0);
            return Array.Empty<PropertySearchHit>();
        }

        var boundedLimit = _policy.ClampDependencyQueryLimit(limit, DefaultLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = _dataAccess.SearchProperties(projectPath, propertyName, valueFilter, boundedLimit, ct);
            ct.ThrowIfCancellationRequested();
            LogTelemetry("searchProperties", started.ElapsedMilliseconds, limit, boundedLimit, resultCount: result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("searchProperties", started.ElapsedMilliseconds, limit, boundedLimit, cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            LogTelemetry("searchProperties", started.ElapsedMilliseconds, limit, boundedLimit, error: ex.GetType().Name);
            throw;
        }
    }

    private string? RequireProjectPath()
    {
        return _projectPathProvider.CurrentProjectPath;
    }

    private void LogTelemetry(
        string operation,
        long durationMs,
        int? requestedLimit = null,
        int? boundedLimit = null,
        int? requestedDepth = null,
        int? boundedDepth = null,
        int? resultCount = null,
        bool cancelled = false,
        string? error = null)
    {
        var safeRequestedLimit = requestedLimit ?? -1;
        var safeBoundedLimit = boundedLimit ?? -1;
        var safeRequestedDepth = requestedDepth ?? -1;
        var safeBoundedDepth = boundedDepth ?? -1;
        var safeResultCount = resultCount ?? -1;
        var safeError = error ?? string.Empty;

        _logger.Info(
            "Agent capability telemetry: capability={Capability} operation={Operation} durationMs={DurationMs} requestedLimit={RequestedLimit} boundedLimit={BoundedLimit} requestedDepth={RequestedDepth} boundedDepth={BoundedDepth} resultCount={ResultCount} cancelled={Cancelled} error={Error}",
            nameof(DependencyGraphCapability),
            operation,
            durationMs,
            safeRequestedLimit,
            safeBoundedLimit,
            safeRequestedDepth,
            safeBoundedDepth,
            safeResultCount,
            cancelled,
            safeError);
    }
}
