// Color Bridge — OCIO + OIIO wrapper implementation
//
// All C++ exceptions are caught at the extern "C" boundary.
// OCIO processors are cached for repeated transforms (e.g., sRGB→Linear).
// Thread safety: processor cache is protected by a mutex; OCIO processors
// themselves are safe for concurrent reads.

#include "color_bridge.h"

#include <OpenColorIO/OpenColorIO.h>
#include <OpenImageIO/imageio.h>
#include <OpenImageIO/imagebuf.h>
#include <OpenImageIO/imagebufalgo.h>

#include <cstring>
#include <map>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace OCIO = OCIO_NAMESPACE;

// ---------------------------------------------------------------------------
// Global state
// ---------------------------------------------------------------------------

static OCIO::ConstConfigRcPtr g_config;
static std::map<std::pair<std::string, std::string>, OCIO::ConstCPUProcessorRcPtr> g_processor_cache;
static std::mutex g_mutex;
static bool g_initialized = false;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Get or create a cached CPU processor for a (src, dst) color space pair.
/// Caller must hold g_mutex or ensure single-threaded access.
static OCIO::ConstCPUProcessorRcPtr get_processor(
    const std::string& src, const std::string& dst)
{
    auto key = std::make_pair(src, dst);
    auto it = g_processor_cache.find(key);
    if (it != g_processor_cache.end()) {
        return it->second;
    }

    auto processor = g_config->getProcessor(src.c_str(), dst.c_str());
    auto cpu = processor->getDefaultCPUProcessor();
    g_processor_cache[key] = cpu;
    return cpu;
}

/// Detect the source color space of an OIIO ImageBuf from its metadata.
/// Falls back to "sRGB" for 8-bit images and "Linear" for float/half.
static std::string detect_colorspace(const OIIO::ImageBuf& buf)
{
    // OIIO sets "oiio:ColorSpace" when it detects ICC profiles or format hints
    auto cs = buf.spec().get_string_attribute("oiio:ColorSpace", "");
    if (cs.size() > 0) {
        // Normalize common OIIO names to our config names
        if (cs == "sRGB" || cs == "srgb") return "sRGB";
        if (cs == "Linear" || cs == "linear" || cs == "scene_linear") return "Linear";
        if (cs == "AdobeRGB") return "AdobeRGB";
        // Return as-is and hope the OCIO config knows it
        return std::string(cs);
    }

    // No metadata — guess based on bit depth
    auto format = buf.spec().format;
    if (format == OIIO::TypeDesc::FLOAT || format == OIIO::TypeDesc::HALF) {
        return "Linear";  // Float images are typically scene-linear
    }
    return "sRGB";  // 8-bit images are typically sRGB
}

/// Safe string copy into a fixed-size buffer, always null-terminated.
static void safe_strcpy(char* dst, size_t dst_size, const char* src)
{
    if (!dst || dst_size == 0) return;
    std::strncpy(dst, src ? src : "", dst_size - 1);
    dst[dst_size - 1] = '\0';
}

// ---------------------------------------------------------------------------
// OCIO API
// ---------------------------------------------------------------------------

extern "C" {

BRIDGE_API int ocio_init(const char* config_path)
{
    if (!config_path) return -1;

    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_initialized) return 0;  // Idempotent

    try {
        g_config = OCIO::Config::CreateFromFile(config_path);
        g_initialized = true;
        return 0;
    } catch (const OCIO::Exception& e) {
        // OCIO config parse error
        return -1;
    } catch (const std::exception& e) {
        return -1;
    }
}

BRIDGE_API int ocio_transform(
    unsigned char* pixels,
    int width, int height,
    const char* src_colorspace,
    const char* dst_colorspace)
{
    // Validate at boundary (FFI-INTEROP-STANDARDS)
    if (!pixels || width <= 0 || height <= 0) return -1;
    if (!src_colorspace || !dst_colorspace) return -1;
    if (!g_initialized || !g_config) return -1;

    try {
        OCIO::ConstCPUProcessorRcPtr cpu;
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            cpu = get_processor(src_colorspace, dst_colorspace);
        }

        // OCIO works on float data. We need to:
        // 1. Convert RGBA8 to float
        // 2. Apply transform
        // 3. Convert back to RGBA8
        const int pixel_count = width * height;
        std::vector<float> float_buf(pixel_count * 4);

        // RGBA8 → float [0,1]
        for (int i = 0; i < pixel_count * 4; ++i) {
            float_buf[i] = pixels[i] / 255.0f;
        }

        // Apply OCIO transform via PackedImageDesc
        OCIO::PackedImageDesc img(
            float_buf.data(),
            width, height,
            4,                          // numChannels (RGBA)
            OCIO::BIT_DEPTH_F32,
            sizeof(float),              // chanStrideBytes
            sizeof(float) * 4,          // xStrideBytes
            sizeof(float) * 4 * width   // yStrideBytes
        );

        cpu->apply(img);

        // Float → RGBA8 with clamping
        for (int i = 0; i < pixel_count * 4; ++i) {
            float v = float_buf[i];
            if (v < 0.0f) v = 0.0f;
            if (v > 1.0f) v = 1.0f;
            pixels[i] = static_cast<unsigned char>(v * 255.0f + 0.5f);
        }

        return 0;
    } catch (const OCIO::Exception& e) {
        return -1;
    } catch (const std::exception& e) {
        return -1;
    }
}

BRIDGE_API void ocio_shutdown(void)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    g_processor_cache.clear();
    g_config.reset();
    g_initialized = false;
}

// ---------------------------------------------------------------------------
// OIIO API
// ---------------------------------------------------------------------------

BRIDGE_API OiioReadResult* oiio_read_image(
    const char* file_path,
    const char* target_colorspace)
{
    auto* result = new OiioReadResult();
    std::memset(result, 0, sizeof(OiioReadResult));

    // Validate at boundary
    if (!file_path || !target_colorspace) {
        safe_strcpy(result->error_message, sizeof(result->error_message),
                    "null file_path or target_colorspace");
        return result;
    }

    if (!g_initialized || !g_config) {
        safe_strcpy(result->error_message, sizeof(result->error_message),
                    "OCIO not initialized — call ocio_init() first");
        return result;
    }

    try {
        // Read image with OIIO
        OIIO::ImageBuf buf(file_path);
        if (!buf.read(0, 0, /*force*/ true)) {
            safe_strcpy(result->error_message, sizeof(result->error_message),
                        buf.geterror().c_str());
            return result;
        }

        // Detect source color space from metadata
        std::string src_cs = detect_colorspace(buf);
        safe_strcpy(result->detected_colorspace, sizeof(result->detected_colorspace),
                    src_cs.c_str());

        // Convert to float for OCIO processing (if not already float)
        OIIO::ImageBuf float_buf;
        if (buf.spec().format != OIIO::TypeDesc::FLOAT) {
            float_buf = OIIO::ImageBuf(
                OIIO::ImageSpec(buf.spec().width, buf.spec().height, 4, OIIO::TypeDesc::FLOAT));
            OIIO::ImageBufAlgo::channels(float_buf, buf, 4, /*channelorder*/ nullptr);
            if (float_buf.has_error()) {
                // If channel conversion fails, try a simple copy
                float_buf.copy(buf);
            }
        } else {
            float_buf.copy(buf);
        }

        // Ensure 4 channels (RGBA)
        if (float_buf.nchannels() < 4) {
            OIIO::ImageBuf rgba_buf;
            int fill_channels[] = {0, 1, 2, -1};  // -1 = fill with 1.0
            float fill_values[] = {0, 0, 0, 1.0f};
            int nchans = float_buf.nchannels();
            if (nchans == 1) {
                fill_channels[0] = 0;
                fill_channels[1] = 0;
                fill_channels[2] = 0;
                fill_channels[3] = -1;
            } else if (nchans == 3) {
                fill_channels[0] = 0;
                fill_channels[1] = 1;
                fill_channels[2] = 2;
                fill_channels[3] = -1;
            }
            rgba_buf = OIIO::ImageBufAlgo::channels(float_buf, 4, fill_channels, fill_values);
            float_buf = rgba_buf;
        }

        // Apply OCIO transform (src → target)
        if (src_cs != std::string(target_colorspace)) {
            OCIO::ConstCPUProcessorRcPtr cpu;
            {
                std::lock_guard<std::mutex> lock(g_mutex);
                cpu = get_processor(src_cs, target_colorspace);
            }

            int w = float_buf.spec().width;
            int h = float_buf.spec().height;

            // Get writable pointer to float data
            float* data = static_cast<float*>(float_buf.localpixels());
            if (data) {
                OCIO::PackedImageDesc img(
                    data, w, h, 4,
                    OCIO::BIT_DEPTH_F32,
                    sizeof(float),
                    sizeof(float) * 4,
                    sizeof(float) * 4 * w
                );
                cpu->apply(img);
            }
        }

        // Convert to RGBA8
        int w = float_buf.spec().width;
        int h = float_buf.spec().height;
        size_t byte_count = static_cast<size_t>(w) * h * 4;

        result->width = w;
        result->height = h;
        result->pixels = new unsigned char[byte_count];

        // Read pixels as UINT8, clamped to [0,1]
        float_buf.get_pixels(OIIO::ROI::All(), OIIO::TypeDesc::UINT8, result->pixels);

        return result;
    } catch (const OCIO::Exception& e) {
        safe_strcpy(result->error_message, sizeof(result->error_message), e.what());
        return result;
    } catch (const std::exception& e) {
        safe_strcpy(result->error_message, sizeof(result->error_message), e.what());
        return result;
    }
}

BRIDGE_API void oiio_free_result(OiioReadResult* result)
{
    if (!result) return;
    delete[] result->pixels;
    result->pixels = nullptr;
    delete result;
}

} // extern "C"
