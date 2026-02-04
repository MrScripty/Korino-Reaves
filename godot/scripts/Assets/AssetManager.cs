// Asset Manager - Main Facade
//
// Provides a unified API for all asset operations.
// Coordinates between AssetLoader, TreeBuilder, and PropertyService.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetAPI;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using UAssetViewer.Services;

namespace UAssetViewer.Assets;

/// <summary>
/// Main facade for asset operations.
/// Implements IAssetService and ITreeService interfaces.
/// </summary>
public sealed class AssetManager : IAssetService, ITreeService
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Assets");

    private readonly IAppLogger _logger;
    private readonly AssetLoader _loader;
    private readonly TreeBuilder _treeBuilder;
    private readonly PropertyService _propertyService;
    private readonly MappingsManager _mappingsManager;

    private UAsset? _currentAsset;
    private string? _currentPath;
    private bool _isModified;

    public bool IsLoaded => _currentAsset != null;
    public AssetInfo? CurrentAsset => _currentAsset != null ? BuildAssetInfo() : null;

    public AssetManager(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loader = new AssetLoader(logger);
        _treeBuilder = new TreeBuilder(logger);
        _propertyService = new PropertyService(logger);
        _mappingsManager = new MappingsManager(logger);
    }

    /// <summary>
    /// Loads an asset from the specified path.
    /// </summary>
    public async Task<AssetInfo> LoadAsync(string path)
    {
        using var activity = ActivitySource.StartActivity("LoadAsset");
        activity?.SetTag("asset.path", path);

        using (_logger.BeginScope("LoadAsset"))
        {
            _logger.Info("Loading asset: {Path}", path);

            try
            {
                // Close any existing asset
                Close();

                // Load mappings if available
                var mappings = await _mappingsManager.LoadMappingsForAssetAsync(path);

                // Load the asset
                _currentAsset = await _loader.LoadAsync(path, mappings);
                _currentPath = path;
                _isModified = false;

                // Initialize tree builder with the loaded asset
                _treeBuilder.Initialize(_currentAsset);

                var info = BuildAssetInfo();
                _logger.Info("Asset loaded successfully: {ExportCount} exports, {ImportCount} imports",
                    info.ExportCount, info.ImportCount);

                activity?.SetStatus(ActivityStatusCode.Ok);
                return info;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load asset: {Path}", path);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw new AssetLoadException($"Failed to load asset: {path}", ex);
            }
        }
    }

    /// <summary>
    /// Saves the current asset to its original path.
    /// </summary>
    public async Task SaveAsync()
    {
        if (_currentAsset == null || _currentPath == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        await SaveAsAsync(_currentPath);
    }

    /// <summary>
    /// Saves the current asset to a new path.
    /// </summary>
    public async Task SaveAsAsync(string path)
    {
        using var activity = ActivitySource.StartActivity("SaveAsset");
        activity?.SetTag("asset.path", path);

        if (_currentAsset == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        using (_logger.BeginScope("SaveAsset"))
        {
            _logger.Info("Saving asset to: {Path}", path);

            try
            {
                await _loader.SaveAsync(_currentAsset, path);
                _currentPath = path;
                _isModified = false;

                _logger.Info("Asset saved successfully");
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save asset: {Path}", path);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }

    /// <summary>
    /// Closes the current asset.
    /// </summary>
    public void Close()
    {
        if (_currentAsset != null)
        {
            _logger.Info("Closing asset: {Path}", _currentPath!);
            _currentAsset = null;
            _currentPath = null;
            _isModified = false;
            _treeBuilder.Clear();
        }
    }

    /// <summary>
    /// Exports the asset to JSON format.
    /// </summary>
    public async Task ExportJsonAsync(string path)
    {
        if (_currentAsset == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        using var activity = ActivitySource.StartActivity("ExportJson");
        activity?.SetTag("output.path", path);

        _logger.Info("Exporting asset to JSON: {Path}", path);

        try
        {
            var json = _currentAsset.SerializeJson();
            await File.WriteAllTextAsync(path, json);
            _logger.Info("JSON export completed");
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export JSON: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Imports property values from JSON.
    /// </summary>
    public async Task ImportJsonAsync(string path)
    {
        if (_currentAsset == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        using var activity = ActivitySource.StartActivity("ImportJson");
        activity?.SetTag("input.path", path);

        _logger.Info("Importing JSON: {Path}", path);

        try
        {
            var json = await File.ReadAllTextAsync(path);
            _currentAsset = UAsset.DeserializeJson(json);
            _isModified = true;

            // Reinitialize tree builder with updated asset
            _treeBuilder.Initialize(_currentAsset);

            _logger.Info("JSON import completed");
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to import JSON: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Gets the root nodes of the asset tree.
    /// </summary>
    public TreeNode[] GetRootNodes()
    {
        if (_currentAsset == null)
        {
            return Array.Empty<TreeNode>();
        }

        return _treeBuilder.GetRootNodes();
    }

    /// <summary>
    /// Gets the children of a tree node.
    /// </summary>
    public TreeNode[] GetChildren(string nodeId)
    {
        if (_currentAsset == null)
        {
            return Array.Empty<TreeNode>();
        }

        return _treeBuilder.GetChildren(nodeId);
    }

    /// <summary>
    /// Gets the properties for a tree node.
    /// </summary>
    public PropertyValue[] GetProperties(string nodeId)
    {
        if (_currentAsset == null)
        {
            return Array.Empty<PropertyValue>();
        }

        return _propertyService.GetPropertiesForNode(_currentAsset, nodeId);
    }

    /// <summary>
    /// Searches the tree for nodes matching the query.
    /// </summary>
    public TreeNode[] Search(string query)
    {
        if (_currentAsset == null)
        {
            return Array.Empty<TreeNode>();
        }

        return _treeBuilder.Search(query);
    }

    /// <summary>
    /// Gets the path from root to a specific node.
    /// </summary>
    public string[] GetPathToNode(string nodeId)
    {
        if (_currentAsset == null)
        {
            return Array.Empty<string>();
        }

        return _treeBuilder.GetPathToNode(nodeId);
    }

    /// <summary>
    /// Gets a property value by path.
    /// </summary>
    public object? GetPropertyValue(string[] path)
    {
        if (_currentAsset == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        return _propertyService.GetValue(_currentAsset, path);
    }

    /// <summary>
    /// Sets a property value by path.
    /// </summary>
    public void SetPropertyValue(string[] path, object value)
    {
        if (_currentAsset == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        _propertyService.SetValue(_currentAsset, path, value);
        _isModified = true;
        _treeBuilder.InvalidateNode(string.Join("/", path));
    }

    /// <summary>
    /// Gets the underlying UAsset for advanced operations.
    /// </summary>
    public UAsset? CurrentUAsset => _currentAsset;
    internal UAsset? GetRawAsset() => _currentAsset;

    private AssetInfo BuildAssetInfo()
    {
        if (_currentAsset == null || _currentPath == null)
        {
            throw new InvalidOperationException("No asset is currently loaded");
        }

        var fileName = Path.GetFileName(_currentPath);
        var engineVersion = _currentAsset.GetEngineVersion().ToString();
        var assetClass = GetMainExportClass();

        return new AssetInfo(
            FilePath: _currentPath,
            FileName: fileName,
            EngineVersion: engineVersion,
            ExportCount: _currentAsset.Exports.Count,
            ImportCount: _currentAsset.Imports.Count,
            NameCount: _currentAsset.GetNameMapIndexList().Count,
            IsModified: _isModified,
            AssetClass: assetClass
        );
    }

    private string? GetMainExportClass()
    {
        if (_currentAsset == null || _currentAsset.Exports.Count == 0)
        {
            return null;
        }

        // The main export is typically the one with OuterIndex == 0
        foreach (var export in _currentAsset.Exports)
        {
            if (export.OuterIndex.Index == 0)
            {
                var classIndex = export.ClassIndex;
                if (classIndex.IsImport())
                {
                    var import = classIndex.ToImport(_currentAsset);
                    return import?.ObjectName.Value.Value;
                }
            }
        }

        // Fallback to first export
        var firstExport = _currentAsset.Exports[0];
        if (firstExport.ClassIndex.IsImport())
        {
            var import = firstExport.ClassIndex.ToImport(_currentAsset);
            return import?.ObjectName.Value.Value;
        }

        return null;
    }
}

/// <summary>
/// Exception thrown when asset loading fails.
/// </summary>
public class AssetLoadException : Exception
{
    public AssetLoadException(string message) : base(message) { }
    public AssetLoadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when a property is not found.
/// </summary>
public class PropertyNotFoundException : Exception
{
    public string[] Path { get; }

    public PropertyNotFoundException(string[] path)
        : base($"Property not found: {string.Join("/", path)}")
    {
        Path = path;
    }
}

/// <summary>
/// Exception thrown when a property value is invalid.
/// </summary>
public class InvalidPropertyValueException : Exception
{
    public string[] Path { get; }
    public object? Value { get; }

    public InvalidPropertyValueException(string[] path, object? value, string reason)
        : base($"Invalid value for property {string.Join("/", path)}: {reason}")
    {
        Path = path;
        Value = value;
    }
}
