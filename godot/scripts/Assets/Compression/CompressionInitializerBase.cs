// Compression Initializer Base
//
// Provides common functionality for platform-specific compression initializers.

using System;
using System.IO;
using CUE4Parse.Compression;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Base class for compression initializers with shared functionality.
/// </summary>
public abstract class CompressionInitializerBase : ICompressionInitializer
{
    public abstract string PlatformName { get; }

    /// <summary>
    /// Gets the library search paths for this platform.
    /// </summary>
    protected abstract string[] GetLibrarySearchPaths();

    /// <summary>
    /// Gets the installation instructions for this platform.
    /// </summary>
    public abstract string GetInstallationInstructions();

    /// <summary>
    /// Attempts to initialize the compression library by searching known paths.
    /// </summary>
    public virtual bool TryInitialize(IAppLogger? logger = null)
    {
        var searchPaths = GetLibrarySearchPaths();

        foreach (var path in searchPaths)
        {
            if (TryInitializeFromPath(path, logger))
            {
                return true;
            }
        }

        logger?.Warning(
            "Native zlib-ng library not found for {Platform}.\n{Instructions}",
            PlatformName,
            GetInstallationInstructions());

        return false;
    }

    /// <summary>
    /// Attempts to initialize ZlibHelper from a specific path.
    /// </summary>
    protected bool TryInitializeFromPath(string path, IAppLogger? logger)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        logger?.Debug("Found zlib-ng at: {Path}", path);

        try
        {
            ZlibHelper.Initialize(path);
            logger?.Info("Zlib initialized from: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            logger?.Debug("Failed to load zlib from {Path}: {Message}", path, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Gets a path relative to the application base directory.
    /// </summary>
    protected static string GetAppPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }
}
