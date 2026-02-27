// Metadata Capability
//
// Enforces bounded metadata query inputs and delegates to dependency data access.

using System;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability implementation for asset metadata queries.
/// </summary>
public sealed class MetadataCapability : IMetadataCapability
{
    private const int DefaultRowLimit = 200;
    private const int MaxRowLimit = 2000;

    private readonly IProjectPathProvider _projectPathProvider;
    private readonly IDependencyDataAccess _dataAccess;
    private readonly IAppLogger _logger;

    public MetadataCapability(
        IProjectPathProvider projectPathProvider,
        IDependencyDataAccess dataAccess,
        IAppLogger logger)
    {
        _projectPathProvider = projectPathProvider ?? throw new ArgumentNullException(nameof(projectPathProvider));
        _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public AssetMetadataSnapshot? GetAssetMetadata(string assetPath, int rowLimit = DefaultRowLimit)
    {
        var projectPath = _projectPathProvider.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        var boundedLimit = ClampLimit(rowLimit, DefaultRowLimit, MaxRowLimit);
        try
        {
            return _dataAccess.GetAssetMetadata(projectPath, assetPath, boundedLimit);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Metadata query failed for asset path: {Path}", assetPath);
            return null;
        }
    }

    private static int ClampLimit(int requested, int fallback, int max)
    {
        if (requested <= 0)
        {
            return fallback;
        }

        return Math.Min(requested, max);
    }
}
