// Linux Compression Initializer
//
// Handles zlib-ng library initialization on Linux systems.

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Compression initializer for Linux systems.
/// Searches standard Linux library paths for zlib-ng.
/// </summary>
public sealed class LinuxCompressionInitializer : CompressionInitializerBase
{
    public override string PlatformName => "Linux";

    protected override string[] GetLibrarySearchPaths() =>
    [
        // x86_64 Debian/Ubuntu paths
        "/usr/lib/x86_64-linux-gnu/libz-ng.so.2",
        "/usr/lib/x86_64-linux-gnu/libz-ng.so",
        // RHEL/Fedora paths
        "/usr/lib64/libz-ng.so.2",
        "/usr/lib64/libz-ng.so",
        // Generic paths
        "/usr/lib/libz-ng.so.2",
        "/usr/lib/libz-ng.so",
        // Local installation
        "/usr/local/lib/libz-ng.so.2",
        "/usr/local/lib/libz-ng.so",
        // Application directory (bundled library)
        GetAppPath("libz-ng.so.2"),
        GetAppPath("libz-ng.so"),
        GetAppPath("libzlib-ng.so"),
    ];

    public override string GetInstallationInstructions() =>
        "To enable PAK decompression on Linux, install zlib-ng:\n" +
        "  Ubuntu/Debian: sudo apt install zlib-ng-dev\n" +
        "  Fedora/RHEL:   sudo dnf install zlib-ng-devel\n" +
        "  Arch Linux:    sudo pacman -S zlib-ng\n" +
        "  OpenSUSE:      sudo zypper install zlib-ng-devel\n" +
        "Or build from source: https://github.com/zlib-ng/zlib-ng";
}
