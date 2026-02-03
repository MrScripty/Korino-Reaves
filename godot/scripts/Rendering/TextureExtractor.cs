// Texture Extractor - CUE4Parse to Godot Image
//
// Extracts textures from Unreal Engine assets using CUE4Parse
// and converts them to Godot Image format for display.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Godot;
using SkiaSharp;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Information about an extracted texture.
/// </summary>
public sealed record TextureInfo(
    int Width,
    int Height,
    string Format,
    int MipCount,
    bool HasAlpha
);

/// <summary>
/// Extracts textures from UE assets and converts to Godot Image.
/// </summary>
public sealed class TextureExtractor
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Rendering.Texture");

    private readonly IAppLogger _logger;

    public TextureExtractor(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts a texture from a PAK file and converts to Godot Image.
    /// </summary>
    public async Task<Image?> ExtractFromPakAsync(
        DefaultFileProvider provider,
        string assetPath)
    {
        using var activity = ActivitySource.StartActivity("ExtractFromPak");
        activity?.SetTag("texture.path", assetPath);

        _logger.Debug("Extracting texture: {Path}", assetPath);

        try
        {
            // Load the texture asset
            var gameFile = await Task.Run(() => provider.LoadObject<UTexture2D>(assetPath));

            if (gameFile == null)
            {
                _logger.Warning("Texture not found: {Path}", assetPath);
                return null;
            }

            return await ExtractFromTexture2DAsync(gameFile);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract texture: {Path}", assetPath);
            throw;
        }
    }

    /// <summary>
    /// Extracts a Godot Image from a CUE4Parse UTexture2D.
    /// </summary>
    public async Task<Image?> ExtractFromTexture2DAsync(UTexture2D texture)
    {
        using var activity = ActivitySource.StartActivity("ExtractFromTexture2D");
        activity?.SetTag("texture.name", texture.Name);

        try
        {
            // Decode texture using CUE4Parse-Conversion
            var decoded = await Task.Run(() => texture.Decode());

            if (decoded == null)
            {
                _logger.Warning("Failed to decode texture: {Name}", texture.Name);
                return null;
            }

            // Convert SKBitmap to Godot Image
            var image = ConvertToGodotImage(decoded);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("texture.width", image.GetWidth());
            activity?.SetTag("texture.height", image.GetHeight());

            return image;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract texture: {Name}", texture.Name);
            throw;
        }
    }

    /// <summary>
    /// Gets information about a texture without fully decoding it.
    /// </summary>
    public TextureInfo? GetTextureInfo(UTexture2D texture)
    {
        if (texture == null)
        {
            return null;
        }

        var format = texture.Format.ToString();
        var hasAlpha = format.Contains("A8") || format.Contains("BC3")
            || format.Contains("BC7") || format.Contains("DXT5");

        return new TextureInfo(
            Width: texture.SizeX,
            Height: texture.SizeY,
            Format: format,
            MipCount: texture.Mips?.Length ?? 0,
            HasAlpha: hasAlpha
        );
    }

    /// <summary>
    /// Converts an SKBitmap to a Godot Image.
    /// </summary>
    private Image ConvertToGodotImage(SKBitmap bitmap)
    {
        // Get pixel data from SKBitmap
        var width = bitmap.Width;
        var height = bitmap.Height;

        // SKBitmap is typically in BGRA format
        var pixels = bitmap.Bytes;

        // Convert BGRA to RGBA
        var rgbaData = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            rgbaData[i] = pixels[i + 2];     // R from B
            rgbaData[i + 1] = pixels[i + 1]; // G stays
            rgbaData[i + 2] = pixels[i];     // B from R
            rgbaData[i + 3] = pixels[i + 3]; // A stays
        }

        // Create Godot Image from raw data
        var image = Image.CreateFromData(
            width,
            height,
            mipMaps: false,
            Image.Format.Rgba8,
            rgbaData
        );

        return image;
    }

    /// <summary>
    /// Decodes a texture to raw RGBA bytes.
    /// Useful for non-Godot contexts or further processing.
    /// </summary>
    public async Task<byte[]?> DecodeToRgbaAsync(UTexture2D texture)
    {
        using var activity = ActivitySource.StartActivity("DecodeToRgba");

        try
        {
            var decoded = await Task.Run(() => texture.Decode());

            if (decoded == null)
            {
                return null;
            }

            var pixels = decoded.Bytes;
            var rgbaData = new byte[pixels.Length];

            // Convert BGRA to RGBA
            for (int i = 0; i < pixels.Length; i += 4)
            {
                rgbaData[i] = pixels[i + 2];     // R
                rgbaData[i + 1] = pixels[i + 1]; // G
                rgbaData[i + 2] = pixels[i];     // B
                rgbaData[i + 3] = pixels[i + 3]; // A
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return rgbaData;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to decode texture to RGBA");
            throw;
        }
    }

    /// <summary>
    /// Creates a thumbnail of a texture at the specified size.
    /// </summary>
    public async Task<Image?> CreateThumbnailAsync(
        UTexture2D texture,
        int maxSize = 128)
    {
        using var activity = ActivitySource.StartActivity("CreateThumbnail");
        activity?.SetTag("thumbnail.maxSize", maxSize);

        try
        {
            var decoded = await Task.Run(() => texture.Decode());

            if (decoded == null)
            {
                return null;
            }

            // Calculate thumbnail dimensions
            var scale = Math.Min(
                (float)maxSize / decoded.Width,
                (float)maxSize / decoded.Height
            );
            scale = Math.Min(scale, 1.0f); // Don't upscale

            var thumbWidth = (int)(decoded.Width * scale);
            var thumbHeight = (int)(decoded.Height * scale);

            // Resize using SkiaSharp
            using var resized = decoded.Resize(
                new SKImageInfo(thumbWidth, thumbHeight),
                SKFilterQuality.Medium
            );

            if (resized == null)
            {
                return ConvertToGodotImage(decoded);
            }

            return ConvertToGodotImage(resized);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to create thumbnail");
            throw;
        }
    }
}
