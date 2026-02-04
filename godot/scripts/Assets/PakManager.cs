// PAK Manager - PAK File Handling
//
// Uses CUE4Parse to browse and extract files from Unreal Engine PAK archives.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets;

/// <summary>
/// Entry in a PAK file listing.
/// </summary>
public sealed record PakEntry(
    string Path,
    string Name,
    long Size,
    bool IsAsset,
    bool IsDirectory
);

/// <summary>
/// Manages PAK file browsing and extraction using CUE4Parse.
/// </summary>
public sealed class PakManager : IDisposable
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Assets.Pak");

    private readonly IAppLogger _logger;
    private readonly AssetLoader _assetLoader;
    private DefaultFileProvider? _provider;
    private string? _currentPakPath;
    private bool _disposed;

    public bool IsOpen => _provider != null;
    public string? CurrentPath => _currentPakPath;

    public PakManager(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetLoader = new AssetLoader(logger);
    }

    /// <summary>
    /// Opens a PAK file or directory containing PAK files.
    /// </summary>
    public async Task OpenAsync(string path, EGame gameVersion = EGame.GAME_UE5_3)
    {
        using var activity = ActivitySource.StartActivity("OpenPak");
        activity?.SetTag("pak.path", path);
        activity?.SetTag("pak.version", gameVersion.ToString());

        _logger.Info("Opening PAK: {Path}", path);

        try
        {
            // Dispose existing provider
            _provider?.Dispose();
            _provider = null;

            string pakDirectory;
            if (File.Exists(path))
            {
                pakDirectory = Path.GetDirectoryName(path)
                    ?? throw new ArgumentException("Invalid path", nameof(path));
            }
            else if (Directory.Exists(path))
            {
                pakDirectory = path;
            }
            else
            {
                throw new FileNotFoundException("PAK file or directory not found", path);
            }

            // Create file provider
            _provider = new DefaultFileProvider(
                pakDirectory,
                SearchOption.AllDirectories,
                versions: new VersionContainer(gameVersion),
                pathComparer: StringComparer.OrdinalIgnoreCase
            );

            // Initialize the provider
            _provider.Initialize();

            // Mount all PAK files
            await Task.Run(() => _provider.Mount());

            _currentPakPath = path;

            _logger.Info("PAK opened successfully: {FileCount} files",
                _provider.Files.Count);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("pak.fileCount", _provider.Files.Count);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to open PAK: {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Opens a PAK file with an encryption key.
    /// </summary>
    public async Task OpenWithKeyAsync(
        string path,
        string aesKey,
        EGame gameVersion = EGame.GAME_UE5_3)
    {
        using var activity = ActivitySource.StartActivity("OpenPakWithKey");
        activity?.SetTag("pak.path", path);

        await OpenAsync(path, gameVersion);

        if (_provider == null)
        {
            return;
        }

        // Submit encryption key
        try
        {
            var key = new FAesKey(aesKey);
            await Task.Run(() => _provider.SubmitKey(new FGuid(), key));

            _logger.Info("Encryption key submitted successfully");
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to submit encryption key: {Message}", ex.Message);
            // Continue without encryption - some files may still be readable
        }
    }

    /// <summary>
    /// Closes the current PAK file.
    /// </summary>
    public void Close()
    {
        if (_provider != null)
        {
            _logger.Info("Closing PAK: {Path}", _currentPakPath!);
            _provider.Dispose();
            _provider = null;
            _currentPakPath = null;
        }
    }

    /// <summary>
    /// Lists all files in the PAK.
    /// </summary>
    public string[] ListFiles()
    {
        if (_provider == null)
        {
            return Array.Empty<string>();
        }

        return _provider.Files.Keys.ToArray();
    }

    /// <summary>
    /// Lists files in a specific directory within the PAK.
    /// </summary>
    public PakEntry[] ListDirectory(string? directory = null)
    {
        using var activity = ActivitySource.StartActivity("ListDirectory");
        activity?.SetTag("pak.directory", directory ?? "/");

        if (_provider == null)
        {
            return Array.Empty<PakEntry>();
        }

        var normalizedDir = NormalizePath(directory ?? "");
        var entries = new Dictionary<string, PakEntry>();

        foreach (var file in _provider.Files.Keys)
        {
            var filePath = NormalizePath(file);

            // Check if file is in this directory
            if (!string.IsNullOrEmpty(normalizedDir))
            {
                if (!filePath.StartsWith(normalizedDir + "/"))
                {
                    continue;
                }
            }

            // Get relative path from this directory
            var relativePath = string.IsNullOrEmpty(normalizedDir)
                ? filePath
                : filePath.Substring(normalizedDir.Length + 1);

            // Get first segment (immediate child)
            var slashIndex = relativePath.IndexOf('/');
            if (slashIndex > 0)
            {
                // This is a subdirectory
                var dirName = relativePath.Substring(0, slashIndex);
                var dirKey = string.IsNullOrEmpty(normalizedDir)
                    ? dirName
                    : normalizedDir + "/" + dirName;

                if (!entries.ContainsKey(dirKey))
                {
                    entries[dirKey] = new PakEntry(
                        Path: dirKey,
                        Name: dirName,
                        Size: 0,
                        IsAsset: false,
                        IsDirectory: true
                    );
                }
            }
            else
            {
                // This is a file in this directory
                var gameFile = _provider.Files[file];
                entries[filePath] = new PakEntry(
                    Path: filePath,
                    Name: relativePath,
                    Size: gameFile.Size,
                    IsAsset: IsAssetFile(relativePath),
                    IsDirectory: false
                );
            }
        }

        return entries.Values.OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name)
            .ToArray();
    }

    /// <summary>
    /// Extracts a file from the PAK as raw bytes.
    /// </summary>
    public async Task<byte[]> ExtractFileAsync(string filePath)
    {
        using var activity = ActivitySource.StartActivity("ExtractFile");
        activity?.SetTag("pak.filePath", filePath);

        if (_provider == null)
        {
            throw new InvalidOperationException("No PAK file is open");
        }

        _logger.Debug("Extracting file: {FilePath}", filePath);

        try
        {
            var normalizedPath = NormalizePath(filePath);

            if (!_provider.Files.TryGetValue(normalizedPath, out var gameFile))
            {
                throw new FileNotFoundException($"File not found in PAK: {filePath}");
            }

            var data = await Task.Run(() => gameFile.Read());

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("pak.extractedSize", data.Length);

            return data;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract file: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Loads an asset directly from the PAK.
    /// </summary>
    public async Task<UAsset> LoadAssetAsync(
        string assetPath,
        EngineVersion version,
        Usmap? mappings = null)
    {
        using var activity = ActivitySource.StartActivity("LoadAssetFromPak");
        activity?.SetTag("pak.assetPath", assetPath);

        _logger.Debug("Loading asset from PAK: {AssetPath}", assetPath);

        try
        {
            // Extract the .uasset file
            var uassetData = await ExtractFileAsync(assetPath);

            // Try to extract companion .uexp file
            byte[]? uexpData = null;
            var uexpPath = Path.ChangeExtension(assetPath, ".uexp");
            try
            {
                uexpData = await ExtractFileAsync(uexpPath);
            }
            catch (FileNotFoundException)
            {
                // .uexp is optional
            }

            // Load using AssetLoader
            var asset = _assetLoader.LoadFromBytes(
                uassetData,
                Path.GetFileName(assetPath),
                version,
                mappings
            );

            activity?.SetStatus(ActivityStatusCode.Ok);
            return asset;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to load asset from PAK: {AssetPath}", assetPath);
            throw;
        }
    }

    /// <summary>
    /// Searches for files matching a pattern.
    /// </summary>
    public string[] SearchFiles(string pattern)
    {
        if (_provider == null)
        {
            return Array.Empty<string>();
        }

        var lowerPattern = pattern.ToLowerInvariant();
        return _provider.Files.Keys
            .Where(f => f.ToLowerInvariant().Contains(lowerPattern))
            .ToArray();
    }

    /// <summary>
    /// Gets information about the PAK file.
    /// </summary>
    public PakInfo? GetInfo()
    {
        if (_provider == null || _currentPakPath == null)
        {
            return null;
        }

        var totalSize = _provider.Files.Values.Sum(f => f.Size);
        var assetCount = _provider.Files.Keys.Count(IsAssetFile);

        return new PakInfo(
            Path: _currentPakPath,
            FileCount: _provider.Files.Count,
            AssetCount: assetCount,
            TotalSize: totalSize
        );
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static bool IsAssetFile(string path)
    {
        return path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _provider?.Dispose();
            _provider = null;
            _disposed = true;
        }
    }
}

/// <summary>
/// Information about an open PAK file.
/// </summary>
public sealed record PakInfo(
    string Path,
    int FileCount,
    int AssetCount,
    long TotalSize
);
