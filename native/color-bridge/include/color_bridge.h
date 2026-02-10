// Color Bridge — Minimal C API wrapping OCIO + OIIO
//
// Provides two services:
//   1. OCIO: Transform pixel buffers between named color spaces
//   2. OIIO: Read external image files with color space detection + transform
//
// Designed for P/Invoke from C#. All C++ exceptions are caught at the
// extern "C" boundary; errors are signaled via return codes or error strings.

#pragma once

#ifdef _WIN32
  #define BRIDGE_API __declspec(dllexport)
#else
  #define BRIDGE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ---------------------------------------------------------------------------
// OCIO: color space transforms on existing pixel buffers
// ---------------------------------------------------------------------------

/// Initialize OCIO with a config file.
/// Must be called before ocio_transform(). Thread-safe, idempotent after
/// first successful call. Returns 0 on success, -1 on error.
BRIDGE_API int ocio_init(const char* config_path);

/// Transform an RGBA8 pixel buffer in-place between named color spaces.
/// The buffer must be exactly width * height * 4 bytes.
/// Returns 0 on success, -1 on error.
BRIDGE_API int ocio_transform(
    unsigned char* pixels,
    int width,
    int height,
    const char* src_colorspace,
    const char* dst_colorspace
);

/// Shut down OCIO and release all cached processors.
/// Safe to call even if ocio_init() was never called.
BRIDGE_API void ocio_shutdown(void);

// ---------------------------------------------------------------------------
// OIIO: read external image files with color management
// ---------------------------------------------------------------------------

/// Result of reading an image file via OIIO.
/// Caller must free via oiio_free_result().
typedef struct {
    int width;
    int height;
    unsigned char* pixels;            ///< RGBA8 data, width*height*4 bytes
    char detected_colorspace[64];     ///< Source color space from metadata
    char error_message[256];          ///< Non-empty string on failure
} OiioReadResult;

/// Read an image file, detect its color space from embedded metadata
/// (ICC profile, EXIF, EXR attributes), and transform to target_colorspace
/// via OCIO. ocio_init() must have been called first.
///
/// target_colorspace is typically "Linear" (scene-linear, Rec.709 primaries).
/// Returns a heap-allocated OiioReadResult. The caller must free it via
/// oiio_free_result(). On failure, pixels is NULL and error_message is set.
BRIDGE_API OiioReadResult* oiio_read_image(
    const char* file_path,
    const char* target_colorspace
);

/// Free a result returned by oiio_read_image().
BRIDGE_API void oiio_free_result(OiioReadResult* result);

#ifdef __cplusplus
}
#endif
