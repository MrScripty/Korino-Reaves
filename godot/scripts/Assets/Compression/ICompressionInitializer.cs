// Compression Initializer Interface
//
// Defines the contract for platform-specific compression library initialization.

using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Interface for initializing compression libraries required by CUE4Parse.
/// Implementations handle platform-specific library loading.
/// </summary>
public interface ICompressionInitializer
{
    /// <summary>
    /// Gets the platform name this initializer supports.
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Attempts to initialize the compression library.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    bool TryInitialize(IAppLogger? logger = null);

    /// <summary>
    /// Gets instructions for installing the required compression library on this platform.
    /// </summary>
    string GetInstallationInstructions();
}
