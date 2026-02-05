// Compression Initializer Factory
//
// Creates the appropriate compression initializer for the current platform.

using System;
using System.Runtime.InteropServices;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Factory for creating platform-appropriate compression initializers.
/// </summary>
public static class CompressionInitializerFactory
{
    private static ICompressionInitializer? _cachedInitializer;
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// Creates the appropriate compression initializer for the current OS.
    /// </summary>
    public static ICompressionInitializer Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxCompressionInitializer();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCompressionInitializer();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOSCompressionInitializer();
        }

        throw new PlatformNotSupportedException(
            $"Compression initialization is not supported on {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// Ensures compression libraries are initialized for the current platform.
    /// Thread-safe and idempotent - safe to call multiple times.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>True if compression is available, false otherwise.</returns>
    public static bool EnsureInitialized(IAppLogger? logger = null)
    {
        if (_initialized)
        {
            return _cachedInitializer != null;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                return _cachedInitializer != null;
            }

            logger?.Info("Initializing compression libraries for {OS}...",
                RuntimeInformation.OSDescription);

            try
            {
                var initializer = Create();

                if (initializer.TryInitialize(logger))
                {
                    _cachedInitializer = initializer;
                    logger?.Info("Compression libraries initialized successfully for {Platform}",
                        initializer.PlatformName);
                }
                else
                {
                    logger?.Warning("Compression initialization failed for {Platform}. " +
                        "Compressed PAK files may not extract correctly.",
                        initializer.PlatformName);
                }
            }
            catch (PlatformNotSupportedException ex)
            {
                logger?.Warning("Platform not supported for compression: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Unexpected error during compression initialization");
            }

            _initialized = true;
            return _cachedInitializer != null;
        }
    }

    /// <summary>
    /// Gets whether compression has been successfully initialized.
    /// </summary>
    public static bool IsInitialized => _initialized && _cachedInitializer != null;
}
