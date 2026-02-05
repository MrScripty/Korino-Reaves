// Windows Compression Initializer
//
// Handles zlib-ng library initialization on Windows systems.

using System;
using CUE4Parse.Compression;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Compression initializer for Windows systems.
/// Attempts to find or download zlib-ng2.dll.
/// </summary>
public sealed class WindowsCompressionInitializer : CompressionInitializerBase
{
    public override string PlatformName => "Windows";

    protected override string[] GetLibrarySearchPaths() =>
    [
        // Application directory
        GetAppPath("zlib-ng2.dll"),
        // Current working directory
        "zlib-ng2.dll",
    ];

    public override bool TryInitialize(IAppLogger? logger = null)
    {
        // First try to find existing DLL
        if (base.TryInitialize(logger))
        {
            return true;
        }

        // On Windows, attempt to download if not found
        return TryDownloadLibrary(logger);
    }

    private bool TryDownloadLibrary(IAppLogger? logger)
    {
        logger?.Info("Attempting to download zlib-ng2.dll...");

        try
        {
            ZlibHelper.DownloadDll();
            logger?.Info("Successfully downloaded zlib-ng2.dll");
            return true;
        }
        catch (Exception ex)
        {
            logger?.Warning("Failed to download zlib-ng2.dll: {Message}", ex.Message);
            return false;
        }
    }

    public override string GetInstallationInstructions() =>
        "To enable PAK decompression on Windows:\n" +
        "  1. Ensure internet access for automatic download, or\n" +
        "  2. Download zlib-ng2.dll manually from:\n" +
        "     https://github.com/NotOfficer/Zlib-ng.NET/releases\n" +
        "  3. Place the DLL in the application directory";
}
