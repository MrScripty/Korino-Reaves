// Color Space Manager — C# wrapper for the OCIO+OIIO native bridge
//
// Provides two services:
//   1. TransformImage: Convert Godot Image pixel data between color spaces via OCIO
//   2. ReadExternalImage: Read arbitrary image files via OIIO with color space detection
//
// All P/Invoke declarations are isolated in this class (FFI-INTEROP-STANDARDS).
// Symmetric init/shutdown. Thread-safe via the native library's internal mutex.

using System;
using System.Runtime.InteropServices;
using Godot;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Result of reading an external image via OIIO.
/// Matches the native OiioReadResult struct layout.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct OiioReadResultNative
{
    public int Width;
    public int Height;
    public IntPtr Pixels;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string DetectedColorspace;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string ErrorMessage;
}

/// <summary>
/// Managed wrapper around the native OCIO+OIIO color bridge library.
/// Provides color space transforms for Godot Images and external file reading.
/// </summary>
public sealed class ColorSpaceManager : IDisposable
{
    // -----------------------------------------------------------------------
    // P/Invoke declarations — all unsafe FFI isolated here
    // -----------------------------------------------------------------------

    private const string LibName = "libcolor_bridge";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ocio_init(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string configPath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ocio_transform(
        byte[] pixels, int width, int height,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string srcColorspace,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dstColorspace);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ocio_shutdown();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr oiio_read_image(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string targetColorspace);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oiio_free_result(IntPtr result);

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    private readonly IAppLogger _logger;
    private bool _initialized;
    private bool _disposed;

    public ColorSpaceManager(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Whether the native OCIO bridge was successfully initialized.
    /// </summary>
    public bool IsAvailable => _initialized && !_disposed;

    // -----------------------------------------------------------------------
    // Initialization / Shutdown (symmetric, per FFI-INTEROP-STANDARDS)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Attempts to initialize the OCIO native bridge with the given config file.
    /// Thread-safe and idempotent after first success.
    /// </summary>
    /// <returns>True if OCIO is available, false otherwise.</returns>
    public bool TryInitialize(string configPath)
    {
        if (_initialized) return true;
        if (_disposed) return false;

        try
        {
            var result = ocio_init(configPath);
            if (result == 0)
            {
                _initialized = true;
                _logger.Info("OCIO initialized with config: {Path}", configPath);
                return true;
            }

            _logger.Warning("OCIO initialization failed (native returned {Code})", result);
            return false;
        }
        catch (DllNotFoundException)
        {
            _logger.Warning("Native color bridge library not found (libcolor_bridge.so). " +
                "Color space transforms will use Godot fallback.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to initialize color bridge: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Shuts down OCIO and releases native resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_initialized)
        {
            try
            {
                ocio_shutdown();
                _logger.Debug("OCIO shut down");
            }
            catch (Exception ex)
            {
                _logger.Warning("Error during OCIO shutdown: {Error}", ex.Message);
            }
            _initialized = false;
        }
    }

    // -----------------------------------------------------------------------
    // OCIO: Transform existing pixel data
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transforms a Godot Image's pixel data between color spaces via OCIO.
    /// The image must be in RGBA8 format. Modifies the image in-place by
    /// replacing its data with the transformed result.
    /// </summary>
    /// <param name="image">The Godot Image to transform (RGBA8 format).</param>
    /// <param name="srcColorspace">Source color space name (e.g., "sRGB").</param>
    /// <param name="dstColorspace">Target color space name (e.g., "Linear").</param>
    /// <returns>
    /// A new Image with transformed data, or null on failure.
    /// Caller should fall back to Godot's built-in conversion on null.
    /// </returns>
    public Image? TransformImage(Image image, string srcColorspace, string dstColorspace)
    {
        if (!IsAvailable) return null;
        if (image == null) return null;

        try
        {
            var width = image.GetWidth();
            var height = image.GetHeight();

            // Copy pixel data to managed buffer (FFI rule: copy foreign data)
            var data = image.GetData();
            if (data.Length != width * height * 4)
            {
                _logger.Warning("Image data size mismatch: expected {Expected}, got {Actual}",
                    width * height * 4, data.Length);
                return null;
            }

            var result = ocio_transform(data, width, height, srcColorspace, dstColorspace);
            if (result != 0)
            {
                _logger.Warning("OCIO transform failed ({Src}→{Dst})", srcColorspace, dstColorspace);
                return null;
            }

            // Create new Image from transformed data
            return Image.CreateFromData(width, height, false, Image.Format.Rgba8, data);
        }
        catch (Exception ex)
        {
            _logger.Warning("OCIO transform exception: {Error}", ex.Message);
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // OIIO: Read external image files with color management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads an external image file via OIIO, detects its color space from
    /// embedded metadata (ICC profile, EXIF), and transforms to the target
    /// color space via OCIO.
    /// </summary>
    /// <param name="filePath">Path to the image file.</param>
    /// <param name="targetColorspace">Target color space (typically "Linear").</param>
    /// <param name="detectedColorspace">The source color space detected from metadata.</param>
    /// <returns>A Godot Image in the target color space, or null on failure.</returns>
    public Image? ReadExternalImage(string filePath, string targetColorspace,
        out string? detectedColorspace)
    {
        detectedColorspace = null;

        if (!IsAvailable) return null;
        if (string.IsNullOrEmpty(filePath)) return null;

        IntPtr resultPtr = IntPtr.Zero;

        try
        {
            resultPtr = oiio_read_image(filePath, targetColorspace);
            if (resultPtr == IntPtr.Zero)
            {
                _logger.Warning("OIIO returned null for: {Path}", filePath);
                return null;
            }

            // Marshal the native struct — copy data to managed memory immediately
            var native = Marshal.PtrToStructure<OiioReadResultNative>(resultPtr);

            // Check for error
            if (!string.IsNullOrEmpty(native.ErrorMessage))
            {
                _logger.Warning("OIIO error reading {Path}: {Error}",
                    filePath, native.ErrorMessage);
                return null;
            }

            if (native.Pixels == IntPtr.Zero || native.Width <= 0 || native.Height <= 0)
            {
                _logger.Warning("OIIO returned empty result for: {Path}", filePath);
                return null;
            }

            detectedColorspace = native.DetectedColorspace;

            // Copy pixel data from native buffer to managed array
            var byteCount = native.Width * native.Height * 4;
            var pixels = new byte[byteCount];
            Marshal.Copy(native.Pixels, pixels, 0, byteCount);

            _logger.Info("Read external image: {Path} ({W}x{H}, detected={CS})",
                filePath, native.Width, native.Height,
                detectedColorspace ?? "unknown");

            return Image.CreateFromData(
                native.Width, native.Height, false, Image.Format.Rgba8, pixels);
        }
        catch (DllNotFoundException)
        {
            _logger.Warning("Native color bridge not available for OIIO read");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning("OIIO read exception for {Path}: {Error}", filePath, ex.Message);
            return null;
        }
        finally
        {
            // Free native memory (symmetric cleanup)
            if (resultPtr != IntPtr.Zero)
            {
                try { oiio_free_result(resultPtr); }
                catch { /* best effort */ }
            }
        }
    }
}
