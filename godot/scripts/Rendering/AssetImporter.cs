// Asset Importer - Pre-extraction cache builder
//
// Queries the dependency database for all unique textures, meshes, and
// materials, extracts them via CUE4Parse, and saves as Godot .res files.
// At level-load time ResourceLoader.Load<T>() replaces the CUE4Parse pipeline.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Meshes.PSK;
using Godot;
using UAssetViewer.Assets.Compression;
using UAssetViewer.Bridge;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Rendering;

/// <summary>
/// Progress information for the import pipeline.
/// </summary>
public sealed record ImportProgress(int Current, int Total, string CurrentFile, string Phase);

/// <summary>
/// Extracts all unique textures, meshes, and materials from a project's
/// dependency database and saves them as Godot .res files in an asset cache.
/// </summary>
public sealed class AssetImporter
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Rendering.AssetImporter");

    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly TextureExtractor _textureExtractor;
    private readonly MeshExtractor _meshExtractor;
    private readonly MaterialExtractor _materialExtractor;

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    /// <summary>
    /// True while an import operation is running.
    /// </summary>
    public bool IsImporting { get; private set; }

    public AssetImporter(
        IAppLogger logger,
        IpcDispatcher dispatcher,
        TextureExtractor textureExtractor,
        MeshExtractor meshExtractor,
        MaterialExtractor materialExtractor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _textureExtractor = textureExtractor ?? throw new ArgumentNullException(nameof(textureExtractor));
        _meshExtractor = meshExtractor ?? throw new ArgumentNullException(nameof(meshExtractor));
        _materialExtractor = materialExtractor ?? throw new ArgumentNullException(nameof(materialExtractor));
    }

    // =================================================================
    // Main-thread processing
    // =================================================================

    /// <summary>
    /// Called from MainController._Process() to drain the save queue on the
    /// main thread. Processes at most 5 items per frame to avoid stalls.
    /// </summary>
    public void ProcessFrame()
    {
        int processed = 0;
        while (processed < 5 && _mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing main-thread queue item");
            }
            processed++;
        }
    }

    // =================================================================
    // Import pipeline
    // =================================================================

    /// <summary>
    /// Imports all unique textures, meshes, and materials from the dependency
    /// database into the asset cache as Godot .res files.
    /// Runs in 3 phases: textures, meshes, materials (dependency order).
    /// </summary>
    public async Task ImportAllAsync(
        string projectPath,
        EGame eGameVersion,
        DependencyDatabase db,
        AssetCache cache,
        Action<ImportProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("ImportAll");
        activity?.SetTag("import.projectPath", projectPath);
        activity?.SetTag("import.eGameVersion", eGameVersion.ToString());

        IsImporting = true;

        try
        {
            // ---------------------------------------------------------
            // Create CUE4Parse provider
            // ---------------------------------------------------------
            _logger.Info("AssetImporter creating CUE4Parse provider: {Path}, version: {Version}",
                projectPath, eGameVersion);

            CompressionInitializerFactory.EnsureInitialized(_logger);

            using var provider = new DefaultFileProvider(
                projectPath,
                SearchOption.AllDirectories,
                versions: new VersionContainer(eGameVersion),
                pathComparer: StringComparer.OrdinalIgnoreCase
            );

            provider.Initialize();
            await Task.Run(() => provider.Mount(), ct);

            _logger.Info("AssetImporter provider ready: {FileCount} files", provider.Files.Count);

            if (provider.Files.Count == 0)
            {
                _logger.Error("Provider mounted 0 files — aborting import");
                return;
            }

            // Build a lookup from filename (no ext) → provider key for fast path resolution
            var filenameLookup = BuildFilenameLookup(provider);

            var projectDirName = Path.GetFileName(
                projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // ---------------------------------------------------------
            // Phase 1 — Textures
            // ---------------------------------------------------------
            ct.ThrowIfCancellationRequested();
            await ImportTexturesAsync(db, cache, provider, filenameLookup, projectDirName, onProgress, ct);

            // ---------------------------------------------------------
            // Phase 2 — Meshes
            // ---------------------------------------------------------
            ct.ThrowIfCancellationRequested();
            await ImportMeshesAsync(db, cache, provider, filenameLookup, projectDirName, onProgress, ct);

            // ---------------------------------------------------------
            // Phase 3 — Materials
            // ---------------------------------------------------------
            ct.ThrowIfCancellationRequested();
            await ImportMaterialsAsync(db, cache, provider, filenameLookup, projectDirName, onProgress, ct);

            // ---------------------------------------------------------
            // Finalize
            // ---------------------------------------------------------
            await RunOnMainThreadAsync(() => cache.SaveManifest());
            _logger.Info("AssetImporter completed all phases");
        }
        finally
        {
            IsImporting = false;
        }
    }

    // =================================================================
    // Phase 1 — Textures
    // =================================================================

    private async Task ImportTexturesAsync(
        DependencyDatabase db,
        AssetCache cache,
        DefaultFileProvider provider,
        Dictionary<string, string> filenameLookup,
        string projectDirName,
        Action<ImportProgress>? onProgress,
        CancellationToken ct)
    {
        var texturePaths = db.GetUniqueTargetsByRefType("Texture2D");
        _logger.Info("Phase 1 — Textures: {Count} unique assets", texturePaths.Count);

        for (int i = 0; i < texturePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var assetPath = texturePaths[i];
            var progress = new ImportProgress(i + 1, texturePaths.Count, assetPath, "Textures");
            ReportProgress(progress, onProgress);

            try
            {
                var loadPath = ResolveDbPathToProviderKey(assetPath, provider, filenameLookup, projectDirName);
                if (loadPath == null)
                {
                    _logger.Warning("Texture not found in provider: {Path}", assetPath);
                    continue;
                }

                var texture = await Task.Run(() => provider.LoadPackageObject<UTexture2D>(loadPath), ct);
                if (texture == null)
                {
                    _logger.Warning("Failed to load texture: {Path}", assetPath);
                    continue;
                }

                var image = await _textureExtractor.ExtractFromTexture2DAsync(texture);
                if (image == null)
                {
                    _logger.Warning("Failed to extract texture image: {Path}", assetPath);
                    continue;
                }

                // Build relative .res path mirroring DB structure
                var relativeResPath = "textures/" + SanitizePath(assetPath) + ".res";
                var absoluteResPath = Path.Combine(cache.CacheDirectory, relativeResPath);

                EnsureDirectoryExists(absoluteResPath);

                // Create ImageTexture and save on main thread
                await RunOnMainThreadAsync(() =>
                {
                    var imageTexture = ImageTexture.CreateFromImage(image);
                    ResourceSaver.Save(imageTexture, absoluteResPath);
                });

                cache.RegisterTexture(assetPath, relativeResPath);
                _logger.Debug("Cached texture: {Path} → {ResPath}", assetPath, relativeResPath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning("Failed to import texture '{Path}': {Error}", assetPath, ex.Message);
            }
        }

        _logger.Info("Phase 1 complete: textures processed");
    }

    // =================================================================
    // Phase 2 — Meshes
    // =================================================================

    private async Task ImportMeshesAsync(
        DependencyDatabase db,
        AssetCache cache,
        DefaultFileProvider provider,
        Dictionary<string, string> filenameLookup,
        string projectDirName,
        Action<ImportProgress>? onProgress,
        CancellationToken ct)
    {
        var meshPaths = db.GetUniqueTargetsByRefType("StaticMesh", "SkeletalMesh");
        _logger.Info("Phase 2 — Meshes: {Count} unique assets", meshPaths.Count);

        for (int i = 0; i < meshPaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var assetPath = meshPaths[i];
            var progress = new ImportProgress(i + 1, meshPaths.Count, assetPath, "Meshes");
            ReportProgress(progress, onProgress);

            try
            {
                var loadPath = ResolveDbPathToProviderKey(assetPath, provider, filenameLookup, projectDirName);
                if (loadPath == null)
                {
                    _logger.Warning("Mesh not found in provider: {Path}", assetPath);
                    continue;
                }

                // Try static mesh first, then skeletal mesh
                MeshExtractionResult? result = null;
                try
                {
                    var staticMesh = await Task.Run(() => provider.LoadPackageObject<UStaticMesh>(loadPath), ct);
                    if (staticMesh != null)
                    {
                        result = await _meshExtractor.ExtractStaticMeshAsync(staticMesh);
                    }
                }
                catch
                {
                    // Not a static mesh — try skeletal
                }

                if (result == null)
                {
                    try
                    {
                        var skelMesh = await Task.Run(() => provider.LoadPackageObject<USkeletalMesh>(loadPath), ct);
                        if (skelMesh != null)
                        {
                            result = await _meshExtractor.ExtractSkeletalMeshAsync(skelMesh);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to extract mesh '{Path}': {Error}", assetPath, ex.Message);
                        continue;
                    }
                }

                if (result == null)
                {
                    _logger.Warning("Failed to extract mesh: {Path}", assetPath);
                    continue;
                }

                // Record surface-to-material mapping from sections
                var surfaceMaterialPaths = new string?[result.Sections.Length];
                for (int s = 0; s < result.Sections.Length; s++)
                {
                    var section = result.Sections[s];
                    surfaceMaterialPaths[s] = ResolveSectionMaterialPath(section);
                }

                // Save mesh .res on main thread
                var relativeResPath = "meshes/" + SanitizePath(assetPath) + ".res";
                var absoluteResPath = Path.Combine(cache.CacheDirectory, relativeResPath);
                EnsureDirectoryExists(absoluteResPath);

                await RunOnMainThreadAsync(() =>
                {
                    ResourceSaver.Save(result.Mesh, absoluteResPath);
                });

                cache.RegisterMesh(assetPath, relativeResPath, surfaceMaterialPaths);
                _logger.Debug("Cached mesh: {Path} → {ResPath} ({Surfaces} surfaces)",
                    assetPath, relativeResPath, surfaceMaterialPaths.Length);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning("Failed to import mesh '{Path}': {Error}", assetPath, ex.Message);
            }
        }

        _logger.Info("Phase 2 complete: meshes processed");
    }

    // =================================================================
    // Phase 3 — Materials
    // =================================================================

    private async Task ImportMaterialsAsync(
        DependencyDatabase db,
        AssetCache cache,
        DefaultFileProvider provider,
        Dictionary<string, string> filenameLookup,
        string projectDirName,
        Action<ImportProgress>? onProgress,
        CancellationToken ct)
    {
        var materialPaths = db.GetUniqueTargetsByRefType("Material", "MaterialInstanceConstant");
        _logger.Info("Phase 3 — Materials: {Count} unique assets", materialPaths.Count);

        for (int i = 0; i < materialPaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var assetPath = materialPaths[i];
            var progress = new ImportProgress(i + 1, materialPaths.Count, assetPath, "Materials");
            ReportProgress(progress, onProgress);

            try
            {
                var loadPath = ResolveDbPathToProviderKey(assetPath, provider, filenameLookup, projectDirName);
                if (loadPath == null)
                {
                    _logger.Warning("Material not found in provider: {Path}", assetPath);
                    continue;
                }

                var materialInterface = await Task.Run(
                    () => provider.LoadPackageObject<UMaterialInterface>(loadPath), ct);
                if (materialInterface == null)
                {
                    _logger.Warning("Failed to load material: {Path}", assetPath);
                    continue;
                }

                // Extract material parameters
                var matParams = new CMaterialParams();
                materialInterface.GetParams(matParams);

                // If texture slots are all null and it's an instance, try provider-based resolution
                if (matParams.IsNull && materialInterface is UMaterialInstanceConstant mic)
                {
                    await ResolveTextureParamsViaProviderAsync(mic, matParams, provider);
                }

                // Build the StandardMaterial3D on the main thread (needs ResourceLoader)
                var relativeResPath = "materials/" + SanitizePath(assetPath) + ".res";
                var absoluteResPath = Path.Combine(cache.CacheDirectory, relativeResPath);
                EnsureDirectoryExists(absoluteResPath);

                await RunOnMainThreadAsync(() =>
                {
                    var material = BuildCachedMaterial(matParams, cache, materialInterface.Name);
                    ResourceSaver.Save(material, absoluteResPath);
                });

                cache.RegisterMaterial(assetPath, relativeResPath);
                _logger.Debug("Cached material: {Path} → {ResPath}", assetPath, relativeResPath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning("Failed to import material '{Path}': {Error}", assetPath, ex.Message);
            }
        }

        _logger.Info("Phase 3 complete: materials processed");
    }

    // =================================================================
    // Material building (runs on main thread via queue)
    // =================================================================

    /// <summary>
    /// Builds a StandardMaterial3D using cached .res textures via ResourceLoader.
    /// Mirrors MaterialExtractor.BuildStandardMaterial3D but loads textures from cache.
    /// </summary>
    private StandardMaterial3D BuildCachedMaterial(
        CMaterialParams matParams, AssetCache cache, string? materialName)
    {
        var material = new StandardMaterial3D();

        // --- Albedo (Diffuse) ---
        var diffuseTexPath = ResolveTextureFromParams(matParams.Diffuse, cache);
        if (diffuseTexPath != null)
        {
            material.AlbedoTexture = ResourceLoader.Load<ImageTexture>(diffuseTexPath);
        }

        if (matParams.DiffuseColor.HasValue)
        {
            var dc = matParams.DiffuseColor.Value;
            material.AlbedoColor = new Color(dc.R, dc.G, dc.B, dc.A);
        }

        // --- Normal Map ---
        var normalTex = matParams.Normal as UTexture2D;
        if (normalTex != null)
        {
            var normalTexAssetPath = NormalizeUePathToDbPath(normalTex.GetPathName());
            var normalResPath = cache.GetNormalTexturePath(normalTexAssetPath);

            if (normalResPath == null)
            {
                // No normal variant cached yet — create one by flipping green channel
                var originalResPath = cache.GetTexturePath(normalTexAssetPath);
                if (originalResPath != null)
                {
                    try
                    {
                        var originalTex = ResourceLoader.Load<ImageTexture>(originalResPath);
                        if (originalTex != null)
                        {
                            var normalImage = FlipNormalMapGreenChannel(originalTex.GetImage());
                            var normalImageTex = ImageTexture.CreateFromImage(normalImage);

                            var relNormalResPath = "textures/" + SanitizePath(normalTexAssetPath) + ".normal.res";
                            var absNormalResPath = Path.Combine(cache.CacheDirectory, relNormalResPath);
                            EnsureDirectoryExists(absNormalResPath);

                            ResourceSaver.Save(normalImageTex, absNormalResPath);
                            cache.RegisterNormalTexture(normalTexAssetPath, relNormalResPath);

                            normalResPath = cache.GetNormalTexturePath(normalTexAssetPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to create normal map variant for '{Path}': {Error}",
                            normalTexAssetPath, ex.Message);
                    }
                }
            }

            if (normalResPath != null)
            {
                material.NormalEnabled = true;
                material.NormalTexture = ResourceLoader.Load<ImageTexture>(normalResPath);
            }
        }

        // --- Roughness / Metallic ---
        material.Roughness = matParams.RoughnessValue;
        material.Metallic = matParams.MetallicValue;

        if (matParams.SpecularValue > 0f)
        {
            material.MetallicSpecular = matParams.SpecularValue;
        }

        // --- Emissive ---
        var emissiveTexPath = ResolveTextureFromParams(matParams.Emissive, cache);
        if (emissiveTexPath != null)
        {
            material.EmissionEnabled = true;
            material.EmissionTexture = ResourceLoader.Load<ImageTexture>(emissiveTexPath);
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
        else if (matParams.Opacity != null)
        {
            var opacityTexPath = ResolveTextureFromParams(matParams.Opacity, cache);
            if (opacityTexPath != null)
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = 0.5f;
            }
        }

        material.ResourceName = materialName ?? "Material";
        return material;
    }

    /// <summary>
    /// Resolves a CUE4Parse texture reference to a cached .res absolute path.
    /// Returns null if the texture is not in the cache.
    /// </summary>
    private static string? ResolveTextureFromParams(CUE4Parse.UE4.Assets.Exports.Material.UUnrealMaterial? textureMaterial, AssetCache cache)
    {
        if (textureMaterial is not UTexture2D texture2D) return null;

        var assetPath = NormalizeUePathToDbPath(texture2D.GetPathName());
        return cache.GetTexturePath(assetPath);
    }

    // =================================================================
    // Texture param resolution via provider (cross-package imports)
    // =================================================================

    /// <summary>
    /// Manually resolves texture references from a UMaterialInstanceConstant
    /// by searching the provider for each texture by name. Same approach as
    /// MaterialExtractor.ResolveTextureParamsViaProviderAsync.
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

            UTexture2D? texture = null;
            var resolvedObj = texParam.ParameterValue?.ResolvedObject;
            if (resolvedObj != null)
            {
                var texFullPath = resolvedObj.GetPathName();
                if (!string.IsNullOrEmpty(texFullPath) && texFullPath != "None")
                {
                    var loadPath = ResolveFullGamePath(provider, texFullPath);
                    if (loadPath != null)
                    {
                        texture = await Task.Run(() =>
                        {
                            try { return provider.LoadPackageObject<UTexture2D>(loadPath); }
                            catch { return null; }
                        });
                    }
                }
            }

            if (texture == null)
            {
                var path = FindAssetPathByName(provider, textureName);
                if (path != null)
                {
                    texture = await Task.Run(() =>
                    {
                        try { return provider.LoadPackageObject<UTexture2D>(path); }
                        catch { return null; }
                    });
                }
            }

            if (texture != null)
            {
                AssignTextureToParams(matParams, paramName, texture);
            }
        }
    }

    /// <summary>
    /// Assigns a texture to the appropriate CMaterialParams slot based on
    /// the parameter name. Same heuristics as MaterialExtractor.
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
            matParams.Diffuse ??= texture;
        }
    }

    // =================================================================
    // Normal map processing
    // =================================================================

    /// <summary>
    /// Flips the green channel of a normal map image.
    /// UE4 uses DirectX-style normals (Y+ down), Godot uses OpenGL-style (Y+ up).
    /// Same logic as MaterialExtractor.FlipNormalMapGreenChannel.
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

    // =================================================================
    // Path resolution helpers
    // =================================================================

    /// <summary>
    /// Builds a lookup from filename (without extension) to provider key
    /// for fast path resolution. Multiple files with the same name are
    /// handled first-wins; explicit path resolution is tried first anyway.
    /// </summary>
    private static Dictionary<string, string> BuildFilenameLookup(DefaultFileProvider provider)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in provider.Files.Keys)
        {
            var lastSlash = key.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? key[(lastSlash + 1)..] : key;

            if (fileName.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^7];
            else if (fileName.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^5];

            lookup.TryAdd(fileName, key);
        }

        return lookup;
    }

    /// <summary>
    /// Converts a DB-relative path (e.g. "UE_data/AT/Content/Textures/T_Rock.uasset")
    /// to a CUE4Parse provider key that can be used with LoadPackageObject.
    /// Tries multiple path formats similar to SceneManager.ResolveGamePathForMesh.
    /// </summary>
    private static string? ResolveDbPathToProviderKey(
        string dbPath, DefaultFileProvider provider,
        Dictionary<string, string> filenameLookup, string projectDirName)
    {
        // Strip .uasset/.umap extension
        var normalized = dbPath;
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];
        if (normalized.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^5];

        var withExt = normalized + ".uasset";

        // Try direct matches
        string[] candidates =
        {
            projectDirName + "/" + normalized,
            normalized,
            projectDirName + "/" + withExt,
            withExt,
        };

        foreach (var candidate in candidates)
        {
            if (provider.Files.ContainsKey(candidate))
            {
                // Return without extension for LoadPackageObject
                var result = candidate;
                if (result.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                    result = result[..^7];
                return result;
            }
        }

        // Try filename-only fallback via lookup
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        if (!string.IsNullOrEmpty(fileName) && filenameLookup.TryGetValue(fileName, out var providerKey))
        {
            if (providerKey.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                providerKey = providerKey[..^7];
            return providerKey;
        }

        return null;
    }

    /// <summary>
    /// Resolves a full UE game path to a provider-compatible load path.
    /// Same approach as MaterialExtractor.ResolveFullGamePath.
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

        var normalized = gamePath.TrimStart('/');
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];

        var withExt = normalized + ".uasset";

        if (provider.Files.ContainsKey(normalized)) return normalized;
        if (provider.Files.ContainsKey(withExt)) return normalized;

        // Try prefixed with project directory
        var sampleKey = provider.Files.Keys.FirstOrDefault();
        if (sampleKey != null)
        {
            var firstSlash = sampleKey.IndexOf('/');
            if (firstSlash > 0)
            {
                var prefix = sampleKey[..firstSlash];
                var prefixed = prefix + "/" + normalized;
                var prefixedExt = prefix + "/" + withExt;

                if (provider.Files.ContainsKey(prefixed)) return prefixed;
                if (provider.Files.ContainsKey(prefixedExt)) return prefixed;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches the file provider for an asset file matching the given name.
    /// Returns the load path (without .uasset extension) or null.
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
    /// Resolves a CMeshSection's material reference to a DB-relative asset path.
    /// </summary>
    private static string? ResolveSectionMaterialPath(CMeshSection section)
    {
        if (section.Material == null) return null;

        try
        {
            var pathName = section.Material.GetPathName();
            if (string.IsNullOrEmpty(pathName) || pathName == "None") return null;

            return NormalizeUePathToDbPath(pathName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a UE game path (e.g. "/Game/Materials/MI_Rock") to a
    /// DB-relative path. Finds the "Content/" segment and maps everything
    /// after it, mirroring DependencyScanner.BuildGamePathLookup logic.
    /// </summary>
    private static string NormalizeUePathToDbPath(string uePath)
    {
        // Strip object name suffix
        var dotIndex = uePath.LastIndexOf('.');
        if (dotIndex > 0)
        {
            var afterDot = uePath[(dotIndex + 1)..];
            var beforeDot = uePath[..dotIndex];
            var lastSlash = beforeDot.LastIndexOf('/');
            var packageName = lastSlash >= 0 ? beforeDot[(lastSlash + 1)..] : beforeDot;
            if (afterDot.Equals(packageName, StringComparison.OrdinalIgnoreCase))
            {
                uePath = beforeDot;
            }
        }

        // Normalize
        var normalized = uePath.TrimStart('/').Replace('\\', '/');
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];

        return normalized;
    }

    /// <summary>
    /// Sanitizes a path for use as a filesystem path (replaces unsafe characters).
    /// </summary>
    private static string SanitizePath(string path)
    {
        var sanitized = path.Replace('\\', '/');

        // Strip .uasset extension if present
        if (sanitized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[..^7];

        return sanitized;
    }

    // =================================================================
    // Threading helpers
    // =================================================================

    /// <summary>
    /// Enqueues an action for main-thread execution and awaits its completion.
    /// Uses TaskCompletionSource so background threads can await the result.
    /// </summary>
    private Task RunOnMainThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _mainThreadQueue.Enqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    // =================================================================
    // Progress & IO helpers
    // =================================================================

    private void ReportProgress(ImportProgress progress, Action<ImportProgress>? onProgress)
    {
        onProgress?.Invoke(progress);

        try
        {
            _dispatcher.Send(new IpcMessage(
                MessageTypes.Import,
                "importProgress",
                new
                {
                    current = progress.Current,
                    total = progress.Total,
                    currentFile = progress.CurrentFile,
                    phase = progress.Phase
                },
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch
        {
            // Don't let IPC errors crash the import
        }
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
