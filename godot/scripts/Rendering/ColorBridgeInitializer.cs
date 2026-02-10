// Color Bridge Initializer
//
// Thread-safe singleton that initializes the OCIO+OIIO native bridge.
// Follows the same pattern as CompressionInitializerFactory.
// Graceful degradation: if the native library is unavailable, callers
// fall back to Godot's built-in SrgbToLinear().

using System;
using System.IO;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Factory for initializing the OCIO+OIIO color bridge.
/// Thread-safe and idempotent — safe to call multiple times.
/// </summary>
public static class ColorBridgeInitializer
{
    private static ColorSpaceManager? _manager;
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// OCIO config filename shipped alongside the native library.
    /// </summary>
    private const string ConfigFileName = "ue4_viewer.ocio";

    /// <summary>
    /// Ensures the color bridge is initialized.
    /// Returns the ColorSpaceManager if available, or null if the native
    /// library could not be loaded.
    /// </summary>
    public static ColorSpaceManager? EnsureInitialized(IAppLogger logger)
    {
        if (_initialized)
        {
            return _manager;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                return _manager;
            }

            logger.Info("Initializing OCIO+OIIO color bridge...");

            try
            {
                var manager = new ColorSpaceManager(logger);
                var configPath = FindConfigFile(logger);

                if (configPath == null)
                {
                    logger.Warning("OCIO config file not found ({Config}). " +
                        "Color space transforms will use Godot fallback.",
                        ConfigFileName);
                }
                else if (manager.TryInitialize(configPath))
                {
                    _manager = manager;
                    logger.Info("Color bridge initialized successfully");
                }
                else
                {
                    logger.Warning("Color bridge initialization failed. " +
                        "Color space transforms will use Godot fallback.");
                }
            }
            catch (Exception ex)
            {
                logger.Warning("Color bridge initialization error: {Error}. " +
                    "Color space transforms will use Godot fallback.", ex.Message);
            }

            _initialized = true;
            return _manager;
        }
    }

    /// <summary>
    /// Gets whether the color bridge has been successfully initialized.
    /// </summary>
    public static bool IsInitialized => _initialized && _manager != null;

    /// <summary>
    /// Gets the initialized ColorSpaceManager, or null if unavailable.
    /// </summary>
    public static ColorSpaceManager? Manager => _manager;

    /// <summary>
    /// Searches known locations for the OCIO config file.
    /// </summary>
    private static string? FindConfigFile(IAppLogger logger)
    {
        string[] searchPaths =
        [
            // Next to the application binary (build output)
            Path.Combine(AppContext.BaseDirectory, ConfigFileName),
            // lib/ directory in project root
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "lib", ConfigFileName),
            // native source configs directory
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "native", "color-bridge", "configs", ConfigFileName),
        ];

        foreach (var path in searchPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                logger.Debug("Found OCIO config at: {Path}", fullPath);
                return fullPath;
            }
        }

        // Log what we tried for debugging
        foreach (var path in searchPaths)
        {
            logger.Debug("OCIO config not at: {Path}", Path.GetFullPath(path));
        }

        return null;
    }

    /// <summary>
    /// Shuts down the color bridge and releases native resources.
    /// Called during application shutdown.
    /// </summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            _manager?.Dispose();
            _manager = null;
            _initialized = false;
        }
    }
}
