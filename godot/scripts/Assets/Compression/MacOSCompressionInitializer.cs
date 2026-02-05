// macOS Compression Initializer
//
// Handles zlib-ng library initialization on macOS systems.

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Compression initializer for macOS systems.
/// Searches Homebrew and standard macOS library paths for zlib-ng.
/// </summary>
public sealed class MacOSCompressionInitializer : CompressionInitializerBase
{
    public override string PlatformName => "macOS";

    protected override string[] GetLibrarySearchPaths() =>
    [
        // Homebrew Apple Silicon paths
        "/opt/homebrew/lib/libz-ng.2.dylib",
        "/opt/homebrew/lib/libz-ng.dylib",
        // Homebrew Intel paths
        "/usr/local/lib/libz-ng.2.dylib",
        "/usr/local/lib/libz-ng.dylib",
        // MacPorts paths
        "/opt/local/lib/libz-ng.2.dylib",
        "/opt/local/lib/libz-ng.dylib",
        // Application directory (bundled library)
        GetAppPath("libz-ng.2.dylib"),
        GetAppPath("libz-ng.dylib"),
    ];

    public override string GetInstallationInstructions() =>
        "To enable PAK decompression on macOS, install zlib-ng:\n" +
        "  Homebrew: brew install zlib-ng\n" +
        "  MacPorts: sudo port install zlib-ng\n" +
        "Or build from source: https://github.com/zlib-ng/zlib-ng";
}
