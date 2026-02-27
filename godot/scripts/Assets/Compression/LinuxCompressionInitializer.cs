// Linux Compression Initializer
//
// Handles zlib-ng library initialization on Linux systems.
// Searches bundled, system, and project-local paths, then falls back
// to downloading from the Zlib-ng.NET GitHub releases.

using System;
using System.IO;
using Godot;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets.Compression;

/// <summary>
/// Compression initializer for Linux systems.
/// Searches standard Linux library paths for zlib-ng.
/// </summary>
public sealed class LinuxCompressionInitializer : CompressionInitializerBase
{
    private const string DownloadUrl =
        "https://github.com/NotOfficer/Zlib-ng.NET/releases/download/1.0.0/libz-ng.so";

    public override string PlatformName => "Linux";

    protected override string[] GetLibrarySearchPaths()
    {
        var projectRoot = Path.GetDirectoryName(
            ProjectSettings.GlobalizePath("res://").TrimEnd('/')) ?? "";
        var buildLib = Path.Combine(projectRoot, ".build", "zlib-ng",
            "zlib-ng-2.2.4", "build", "libz-ng.so.2");

        return
        [
            // Application directory (bundled library — checked first)
            GetAppPath("libz-ng.so.2"),
            GetAppPath("libz-ng.so"),
            GetAppPath("libzlib-ng.so"),
            // Project local build
            buildLib,
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
        ];
    }

    public override bool TryInitialize(IAppLogger? logger = null)
    {
        if (base.TryInitialize(logger))
            return true;

        return TryDownloadLibrary(logger);
    }

    private bool TryDownloadLibrary(IAppLogger? logger)
    {
        var targetPath = GetAppPath("libz-ng.so.2");
        logger?.Info("Attempting to download libz-ng.so to {Path}...", targetPath);

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var bytes = client.GetByteArrayAsync(DownloadUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(targetPath, bytes);
            logger?.Info("Downloaded libz-ng.so ({Size} bytes)", bytes.Length);
            return TryInitializeFromPath(targetPath, logger);
        }
        catch (Exception ex)
        {
            logger?.Warning("Failed to download libz-ng.so: {Message}", ex.Message);
            return false;
        }
    }

    public override string GetInstallationInstructions() =>
        "To enable PAK decompression on Linux, install zlib-ng:\n" +
        "  Ubuntu/Debian: sudo apt install zlib-ng-dev\n" +
        "  Fedora/RHEL:   sudo dnf install zlib-ng-devel\n" +
        "  Arch Linux:    sudo pacman -S zlib-ng\n" +
        "  OpenSUSE:      sudo zypper install zlib-ng-devel\n" +
        "Or build from source: https://github.com/zlib-ng/zlib-ng";
}
