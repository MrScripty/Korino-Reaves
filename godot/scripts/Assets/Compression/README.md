# Compression

Cross-platform compression library initialization for CUE4Parse PAK file extraction.

## Purpose

CUE4Parse requires native compression libraries (zlib-ng) to decompress files from Unreal Engine PAK archives. This directory provides a clean, platform-abstracted initialization system that:

- Detects the current operating system
- Searches platform-specific paths for the native library
- Initializes CUE4Parse's ZlibHelper with the appropriate library
- Provides clear installation instructions when the library is missing

## Contents

| File | Description |
|------|-------------|
| `ICompressionInitializer.cs` | Interface contract for platform-specific initializers |
| `CompressionInitializerBase.cs` | Base class with shared search/initialization logic (Template Method pattern) |
| `LinuxCompressionInitializer.cs` | Linux implementation - searches `/usr/lib`, Debian/RHEL paths |
| `WindowsCompressionInitializer.cs` | Windows implementation - auto-downloads DLL if missing |
| `MacOSCompressionInitializer.cs` | macOS implementation - searches Homebrew/MacPorts paths |
| `CompressionInitializerFactory.cs` | Factory that creates the appropriate initializer and manages singleton initialization |

## Design Decisions

### Strategy + Factory Pattern

Platform-specific behavior is encapsulated in separate classes rather than `#if` directives or runtime `if/else` chains. This provides:

- **Single Responsibility**: Each class handles one platform
- **Open/Closed**: New platforms (FreeBSD, etc.) can be added without modifying existing code
- **Testability**: Initializers can be unit tested in isolation

### Template Method Pattern

`CompressionInitializerBase` provides the common algorithm (search paths, try loading), while subclasses override `GetLibrarySearchPaths()` and `GetInstallationInstructions()`.

### Thread-Safe Singleton Initialization

`CompressionInitializerFactory.EnsureInitialized()` uses double-checked locking to ensure the compression library is initialized exactly once, even when called from multiple threads.

## Dependencies

**Internal:**
- `UAssetViewer.Infrastructure.IAppLogger` - For diagnostic logging

**External:**
- `CUE4Parse.Compression.ZlibHelper` - Native library wrapper we're initializing

**Native Libraries (runtime):**
- Linux: `libz-ng.so.2` (from zlib-ng package or built from source)
- Windows: `zlib-ng2.dll` (auto-downloaded or bundled)
- macOS: `libz-ng.dylib` (from Homebrew/MacPorts)

## Usage Examples

### Basic Usage (Automatic)

The `PakManager` automatically initializes compression when opening PAK files:

```csharp
var pakManager = new PakManager(logger);
await pakManager.OpenAsync("/path/to/game.pak"); // Compression initialized here
```

### Manual Initialization

For early initialization or checking status:

```csharp
// Initialize compression libraries
bool success = CompressionInitializerFactory.EnsureInitialized(logger);

// Check if initialized
if (CompressionInitializerFactory.IsInitialized)
{
    // Compression available
}
```

### Adding a New Platform

1. Create a new class extending `CompressionInitializerBase`:

```csharp
public sealed class FreeBSDCompressionInitializer : CompressionInitializerBase
{
    public override string PlatformName => "FreeBSD";

    protected override string[] GetLibrarySearchPaths() =>
    [
        "/usr/local/lib/libz-ng.so.2",
        "/usr/local/lib/libz-ng.so",
        GetAppPath("libz-ng.so.2"),
    ];

    public override string GetInstallationInstructions() =>
        "To enable PAK decompression on FreeBSD:\n" +
        "  pkg install zlib-ng";
}
```

2. Add a case to `CompressionInitializerFactory.Create()`:

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
{
    return new FreeBSDCompressionInitializer();
}
```

## Building zlib-ng

If zlib-ng is not available in your system's package manager, use the build script:

```bash
# From project root
bash scripts/build-zlib-ng.sh
```

This downloads, compiles, and places the library in the correct location.
