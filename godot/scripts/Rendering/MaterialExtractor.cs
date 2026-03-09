// Material Extractor - CUE4Parse to Godot StandardMaterial3D
//
// Extracts material data from Unreal Engine assets using CUE4Parse
// and converts to Godot StandardMaterial3D with PBR textures.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Meshes.PSK;
using Godot;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Result of extracting a material for a single mesh surface.
/// </summary>
public sealed record ExtractedMaterial(
    StandardMaterial3D GodotMaterial,
    string? MaterialName,
    int TextureCount
);

/// <summary>
/// Extracts UE4 materials from CUE4Parse and converts to Godot StandardMaterial3D.
/// </summary>
public sealed class MaterialExtractor
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Rendering.Material");

    private readonly IAppLogger _logger;
    private readonly TextureExtractor _textureExtractor;

    public MaterialExtractor(IAppLogger logger, TextureExtractor textureExtractor,
        ColorSpaceManager? colorSpaceManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textureExtractor = textureExtractor ?? throw new ArgumentNullException(nameof(textureExtractor));
        // colorSpaceManager kept in signature for API compat but no longer needed:
        // Godot's StandardMaterial3D shader handles sRGB→linear via source_color hint.
    }

    /// <summary>
    /// Extracts Godot materials for each section (surface) of a mesh LOD.
    /// Returns an array parallel to the sections array. Each entry may be null
    /// if extraction failed for that section.
    /// </summary>
    /// <param name="sections">Mesh sections with material references.</param>
    /// <param name="provider">File provider for loading cross-package material imports.</param>
    public async Task<ExtractedMaterial?[]> ExtractMaterialsAsync(
        CMeshSection[] sections, DefaultFileProvider? provider = null)
    {
        using var activity = ActivitySource.StartActivity("ExtractMaterials");
        activity?.SetTag("material.sectionCount", sections.Length);

        var results = new ExtractedMaterial?[sections.Length];

        // Deduplicate: multiple sections may reference the same material index
        var materialTasks = new Dictionary<int, Task<ExtractedMaterial?>>();

        for (int i = 0; i < sections.Length; i++)
        {
            var section = sections[i];
            var matIndex = section.MaterialIndex;

            if (section.Material == null && section.MaterialName == null)
            {
                _logger.Debug("Section {Index} has no material reference", i);
                continue;
            }

            if (!materialTasks.ContainsKey(matIndex))
            {
                var sectionCapture = section;
                materialTasks[matIndex] = ExtractSingleMaterialAsync(sectionCapture, provider);
            }
        }

        // Await all unique material extractions in parallel
        await Task.WhenAll(materialTasks.Values);

        // Map results back to sections
        for (int i = 0; i < sections.Length; i++)
        {
            var matIndex = sections[i].MaterialIndex;
            if (materialTasks.TryGetValue(matIndex, out var task))
            {
                results[i] = await task;
            }
        }

        var successCount = results.Count(r => r != null);
        _logger.Info("Extracted {Success}/{Total} materials", successCount, sections.Length);
        activity?.SetTag("material.successCount", successCount);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return results;
    }

    /// <summary>
    /// Extracts a single material from a CMeshSection's Material reference.
    /// </summary>
    private async Task<ExtractedMaterial?> ExtractSingleMaterialAsync(
        CMeshSection section, DefaultFileProvider? provider)
    {
        using var activity = ActivitySource.StartActivity("ExtractSingleMaterial");
        activity?.SetTag("material.name", section.MaterialName ?? "unknown");
        activity?.SetTag("material.index", section.MaterialIndex);

        try
        {
            // Try direct load first (works for exports within the same package)
            var materialInterface = await Task.Run(() =>
                section.Material?.Load<UMaterialInterface>());

            // If direct load fails, try full game path from import table
            if (materialInterface == null && provider != null && section.Material != null)
            {
                var fullPath = section.Material.GetPathName();
                if (!string.IsNullOrEmpty(fullPath))
                {
                    _logger.Debug("Direct load failed for {Name}, trying full path: {Path}",
                        (object)(section.MaterialName ?? "unknown"), (object)fullPath);
                    materialInterface = await LoadMaterialByFullPathAsync(provider, fullPath);
                }
            }

            // Filename-only fallback (last resort)
            if (materialInterface == null && provider != null && section.MaterialName != null)
            {
                _logger.Debug("Full path resolution failed for {Name}, trying filename-only search",
                    section.MaterialName);
                materialInterface = await LoadMaterialByNameAsync(provider, section.MaterialName);
            }

            if (materialInterface == null)
            {
                var attemptedPath = section.Material?.GetPathName();
                _logger.Warning("Could not load material for section {Index}: name={Name}, path={Path}",
                    section.MaterialIndex, section.MaterialName ?? "unknown",
                    attemptedPath ?? "none");
                return null;
            }

            // Extract parameters (textures + scalars).
            // GetParams() populates scalar/color values even when texture references
            // fail to resolve (cross-package imports).
            var matParams = new CMaterialParams();
            materialInterface.GetParams(matParams);

            // If texture slots are all null (cross-package imports couldn't resolve),
            // manually resolve texture references through the provider.
            if (matParams.IsNull && provider != null
                && materialInterface is UMaterialInstanceConstant mic)
            {
                _logger.Debug("Resolving textures via provider for {Name}", section.MaterialName ?? "unknown");
                await ResolveTextureParamsViaProviderAsync(mic, matParams, provider);
            }

            // Decode textures in parallel
            var diffuseTask = DecodeTextureAsync(matParams.Diffuse, "Diffuse");
            var normalTask = DecodeTextureAsync(matParams.Normal, "Normal");
            var specularTask = DecodeTextureAsync(matParams.Specular, "Specular");
            var emissiveTask = DecodeTextureAsync(matParams.Emissive, "Emissive");
            var opacityTask = DecodeTextureAsync(matParams.Opacity, "Opacity");

            await Task.WhenAll(diffuseTask, normalTask, specularTask, emissiveTask, opacityTask);

            var diffuseImage = await diffuseTask;
            var normalImage = await normalTask;
            var specularImage = await specularTask;
            var emissiveImage = await emissiveTask;
            var opacityImage = await opacityTask;

            // Build the Godot material
            var godotMaterial = BuildStandardMaterial3D(
                matParams, diffuseImage, normalImage,
                emissiveImage, opacityImage, section.MaterialName);

            var textureCount = new[] { diffuseImage, normalImage, specularImage, emissiveImage, opacityImage }
                .Count(img => img != null);

            _logger.Info("Material '{Name}': {TexCount} textures, roughness={Rough}, metallic={Metal}",
                section.MaterialName ?? "unknown", textureCount,
                matParams.RoughnessValue, matParams.MetallicValue);

            return new ExtractedMaterial(godotMaterial, section.MaterialName, textureCount);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract material for section {Index} ({Name})",
                section.MaterialIndex, section.MaterialName ?? "unknown");
            return null;
        }
    }

    /// <summary>
    /// Searches the file provider for a material package matching the given name
    /// and loads it. This handles cross-package imports where the material is in
    /// a different .uasset file than the mesh.
    /// </summary>
    private async Task<UMaterialInterface?> LoadMaterialByNameAsync(
        DefaultFileProvider provider, string materialName)
    {
        var loadPath = FindAssetPathByName(provider, materialName);
        if (loadPath == null)
        {
            _logger.Debug("Material package not found in provider: {Name}", materialName);
            return null;
        }

        _logger.Debug("Loading material from provider: {Path}", loadPath);

        return await Task.Run(() =>
        {
            try
            {
                return provider.LoadPackageObject<UMaterialInterface>(loadPath);
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to load material from provider: {Error}", ex.Message);
                return null;
            }
        });
    }

    /// <summary>
    /// Loads a material using its full game path from the import table.
    /// Normalizes the path and tries multiple formats against the provider.
    /// </summary>
    private async Task<UMaterialInterface?> LoadMaterialByFullPathAsync(
        DefaultFileProvider provider, string fullGamePath)
    {
        var loadPath = ResolveFullGamePath(provider, fullGamePath);
        if (loadPath == null)
        {
            _logger.Debug("Material full path not found in provider: {Path}", fullGamePath);
            return null;
        }

        _logger.Debug("Loading material from full path: {Path}", loadPath);

        return await Task.Run(() =>
        {
            try
            {
                return provider.LoadPackageObject<UMaterialInterface>(loadPath);
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to load material from full path: {Error}", ex.Message);
                return null;
            }
        });
    }

    /// <summary>
    /// Resolves a full game path (from import tables) to a provider-compatible load path.
    /// Handles object suffixes, leading slashes, and project directory prefixes.
    /// </summary>
    private static string? ResolveFullGamePath(DefaultFileProvider provider, string gamePath)
    {
        // Strip object name suffix (e.g., "/Game/Mat/M_Rock.M_Rock" -> "/Game/Mat/M_Rock")
        var dotIndex = gamePath.LastIndexOf('.');
        if (dotIndex > 0)
        {
            var afterDot = gamePath[(dotIndex + 1)..];
            var beforeDot = gamePath[..dotIndex];
            var lastSlash = beforeDot.LastIndexOf('/');
            var packageName = lastSlash >= 0 ? beforeDot[(lastSlash + 1)..] : beforeDot;
            if (afterDot.Equals(packageName, StringComparison.OrdinalIgnoreCase))
            {
                gamePath = beforeDot;
            }
        }

        // Normalize: strip leading slash, strip .uasset extension
        var normalized = gamePath.TrimStart('/');
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];

        var withExt = normalized + ".uasset";

        // Try direct matches
        if (provider.Files.ContainsKey(normalized))
            return normalized;
        if (provider.Files.ContainsKey(withExt))
            return normalized;

        // Try prefixed with the project directory (first path segment from provider keys)
        var sampleKey = provider.Files.Keys.FirstOrDefault();
        if (sampleKey != null)
        {
            var firstSlash = sampleKey.IndexOf('/');
            if (firstSlash > 0)
            {
                var prefix = sampleKey[..firstSlash];
                var prefixed = prefix + "/" + normalized;
                var prefixedExt = prefix + "/" + withExt;

                if (provider.Files.ContainsKey(prefixed))
                    return prefixed;
                if (provider.Files.ContainsKey(prefixedExt))
                    return prefixed;
            }
        }

        return null;
    }

    /// <summary>
    /// Manually resolves texture references from a UMaterialInstanceConstant
    /// by searching the provider for each texture by name.
    /// CUE4Parse's GetParams() can't resolve cross-package texture imports
    /// in loose-file providers, so we do it ourselves.
    /// </summary>
    private async Task ResolveTextureParamsViaProviderAsync(
        UMaterialInstanceConstant mic, CMaterialParams matParams,
        DefaultFileProvider provider)
    {
        foreach (var texParam in mic.TextureParameterValues)
        {
            var textureName = texParam.ParameterValue?.Name;
            if (string.IsNullOrEmpty(textureName) || textureName == "None")
                continue;

            var paramName = texParam.Name;
            _logger.Debug("Resolving texture param '{Param}' -> '{Texture}'", paramName, textureName);

            // Try full path from import table first
            UTexture2D? texture = null;
            var resolvedObj = texParam.ParameterValue?.ResolvedObject;
            if (resolvedObj != null)
            {
                var texFullPath = resolvedObj.GetPathName();
                if (!string.IsNullOrEmpty(texFullPath) && texFullPath != "None")
                {
                    texture = await LoadTextureByFullPathAsync(provider, texFullPath);
                }
            }

            // Fall back to name-based search
            texture ??= await LoadTextureByNameAsync(provider, textureName);

            if (texture == null)
            {
                _logger.Warning("Could not resolve texture: param={Param}, name={Name}",
                    paramName, textureName);
                continue;
            }

            AssignTextureToParams(matParams, paramName, texture);
        }
    }

    /// <summary>
    /// Assigns a texture to the appropriate CMaterialParams slot based on
    /// the parameter name, using the same heuristics as CUE4Parse.
    /// </summary>
    private static void AssignTextureToParams(
        CMaterialParams matParams, string paramName, UTexture2D texture)
    {
        var name = paramName.ToLowerInvariant();

        if (name.Contains("diff") || name.Contains("albedo") || name.Contains("color")
            || name.Contains("base") || name.StartsWith("co"))
        {
            matParams.Diffuse ??= texture;
        }
        else if (name.Contains("norm") || name == "nm" || name.StartsWith("nm_"))
        {
            matParams.Normal ??= texture;
        }
        else if (name.Contains("spec") || name.Contains("packed")
            || name.Contains("mrae") || name.Contains("mrs"))
        {
            matParams.Specular ??= texture;
        }
        else if (name.Contains("emiss"))
        {
            matParams.Emissive ??= texture;
        }
        else if (name.Contains("opaci") || name.Contains("mask") || name.Contains("alpha"))
        {
            matParams.Opacity ??= texture;
        }
        else
        {
            // Unknown parameter — use as diffuse if nothing assigned yet
            matParams.Diffuse ??= texture;
        }
    }

    /// <summary>
    /// Searches the provider for a texture by name and loads it.
    /// </summary>
    private async Task<UTexture2D?> LoadTextureByNameAsync(
        DefaultFileProvider provider, string textureName)
    {
        var path = FindAssetPathByName(provider, textureName);
        if (path == null)
        {
            _logger.Debug("Texture not found in provider: {Name}", textureName);
            return null;
        }

        _logger.Debug("Loading texture from provider: {Path}", path);

        return await Task.Run(() =>
        {
            try
            {
                return provider.LoadPackageObject<UTexture2D>(path);
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to load texture from provider: {Error}", ex.Message);
                return null;
            }
        });
    }

    /// <summary>
    /// Loads a texture using its full game path from the import table.
    /// </summary>
    private async Task<UTexture2D?> LoadTextureByFullPathAsync(
        DefaultFileProvider provider, string fullGamePath)
    {
        var loadPath = ResolveFullGamePath(provider, fullGamePath);
        if (loadPath == null) return null;

        _logger.Debug("Loading texture from full path: {Path}", loadPath);

        return await Task.Run(() =>
        {
            try
            {
                return provider.LoadPackageObject<UTexture2D>(loadPath);
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to load texture from full path: {Error}", ex.Message);
                return null;
            }
        });
    }

    /// <summary>
    /// Searches the file provider for an asset file matching the given name.
    /// Returns the load path (without .uasset extension) or null if not found.
    /// </summary>
    private static string? FindAssetPathByName(DefaultFileProvider provider, string assetName)
    {
        foreach (var key in provider.Files.Keys)
        {
            var lastSlash = key.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? key[(lastSlash + 1)..] : key;

            if (fileName.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^7];

            if (fileName.Equals(assetName, StringComparison.OrdinalIgnoreCase))
            {
                var loadPath = key;
                if (loadPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                    loadPath = loadPath[..^7];
                return loadPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a UUnrealMaterial texture slot to a Godot Image.
    /// Returns null if the slot is empty or decoding fails.
    /// </summary>
    private async Task<Image?> DecodeTextureAsync(UUnrealMaterial? textureMaterial, string slotName)
    {
        if (textureMaterial == null) return null;

        try
        {
            if (textureMaterial is UTexture2D texture2D)
            {
                return await _textureExtractor.ExtractFromTexture2DAsync(texture2D);
            }

            _logger.Debug("Texture slot {Slot} is {Type}, not UTexture2D — skipping",
                slotName, textureMaterial.GetType().Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to decode {Slot} texture: {Error}", slotName, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Builds a Godot StandardMaterial3D from extracted CUE4Parse material parameters.
    /// </summary>
    private StandardMaterial3D BuildStandardMaterial3D(
        CMaterialParams matParams,
        Image? diffuseImage,
        Image? normalImage,
        Image? emissiveImage,
        Image? opacityImage,
        string? materialName)
    {
        var material = new StandardMaterial3D();

        // --- Albedo (Diffuse) ---
        if (diffuseImage != null)
        {
            // CUE4Parse decodes textures to sRGB bytes matching UE4's storage format.
            // Upload as-is: Godot creates both UNORM and SRGB GPU texture views for
            // RGBA8 images, and StandardMaterial3D's shader samples albedo with the
            // `source_color` hint which selects the SRGB view — the GPU hardware
            // applies the sRGB→linear conversion automatically during sampling.
            material.AlbedoTexture = ImageTexture.CreateFromImage(diffuseImage);
        }

        if (matParams.DiffuseColor.HasValue)
        {
            var dc = matParams.DiffuseColor.Value;
            material.AlbedoColor = new Color(dc.R, dc.G, dc.B, dc.A);
        }

        // --- Normal Map ---
        if (normalImage != null)
        {
            // Flip green channel: UE4 uses DirectX-style normals (Y+ down),
            // Godot uses OpenGL-style (Y+ up).
            var flipped = FlipNormalMapGreenChannel(normalImage);
            material.NormalEnabled = true;
            material.NormalTexture = ImageTexture.CreateFromImage(flipped);
        }

        // --- Roughness / Metallic ---
        material.Roughness = matParams.RoughnessValue;
        material.Metallic = matParams.MetallicValue;

        if (matParams.SpecularValue > 0f)
        {
            material.MetallicSpecular = matParams.SpecularValue;
        }

        // --- Emissive ---
        if (emissiveImage != null)
        {
            // Same as albedo: keep sRGB bytes, let the GPU handle conversion via
            // the source_color sampler hint in the emission texture uniform.
            material.EmissionEnabled = true;
            material.EmissionTexture = ImageTexture.CreateFromImage(emissiveImage);
            material.Emission = Colors.White;
            material.EmissionEnergyMultiplier = 1.0f;
        }
        else if (matParams.EmissiveColor.HasValue)
        {
            var ec = matParams.EmissiveColor.Value;
            if (ec.R > 0.01f || ec.G > 0.01f || ec.B > 0.01f)
            {
                material.EmissionEnabled = true;
                material.Emission = new Color(ec.R, ec.G, ec.B);
                material.EmissionEnergyMultiplier = 1.0f;
            }
        }

        // --- Transparency ---
        if (matParams.IsTransparent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }
        else if (opacityImage != null)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            material.AlphaScissorThreshold = 0.5f;
        }

        material.ResourceName = materialName ?? "Material";
        return material;
    }

    /// <summary>
    /// Flips the green channel of a normal map image.
    /// UE4 uses DirectX-style normals where green (Y) points down.
    /// Godot/OpenGL expects green (Y) pointing up.
    /// </summary>
    private static Image FlipNormalMapGreenChannel(Image image)
    {
        var data = image.GetData();

        // Image is RGBA8 — green channel is at offset 1 in each 4-byte pixel
        for (int i = 1; i < data.Length; i += 4)
        {
            data[i] = (byte)(255 - data[i]);
        }

        return Image.CreateFromData(
            image.GetWidth(), image.GetHeight(),
            false, Image.Format.Rgba8, data);
    }

    /// <summary>
    /// Creates a default gray material when extraction yields no usable data.
    /// </summary>
    private static ExtractedMaterial BuildDefaultMaterial(string? name)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.7f, 0.7f),
            Roughness = 0.6f,
            Metallic = 0.1f,
            ResourceName = name ?? "Default",
        };
        return new ExtractedMaterial(material, name, 0);
    }
}
