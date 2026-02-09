// Preview Manager - Asset Preview Coordinator
//
// Loads and renders asset previews when files are selected in the project tree.
// Textures are decoded to PNG and sent to the frontend.
// Meshes are rendered in a SubViewport and the frame is captured and sent.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Versions;
using Godot;
using UAssetViewer.Assets.Compression;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Rendering;

/// <summary>
/// Coordinates asset preview loading and rendering.
/// Subscribes to selection changes and sends preview images to the frontend.
/// </summary>
public sealed class PreviewManager : IDisposable
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Rendering.Preview");

    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly TextureExtractor _textureExtractor;
    private readonly MeshExtractor _meshExtractor;
    private readonly MaterialExtractor _materialExtractor;

    private DefaultFileProvider? _fileProvider;
    private string? _projectPath;
    private EGame _currentVersion;

    // SubViewport scene tree for 3D rendering
    private SubViewport? _subViewport;
    private Camera3D? _camera;
    private MeshInstance3D? _meshInstance;
    private DirectionalLight3D? _light;
    private Node? _parentNode;
    private int _captureCountdown;

    // Orbital camera state
    private float _cameraDistance = 3f;
    private float _cameraYaw = 45f;    // degrees
    private float _cameraPitch = -30f;  // degrees
    private Vector3 _cameraTarget = Vector3.Zero;

    private bool _doubleSided = true;
    private bool _disposed;

    private const int ViewportWidth = 1024;
    private const int ViewportHeight = 768;

    public PreviewManager(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _textureExtractor = new TextureExtractor(logger);
        _meshExtractor = new MeshExtractor(logger);
        _materialExtractor = new MaterialExtractor(logger, _textureExtractor);
    }

    /// <summary>
    /// Initializes the SubViewport scene tree and subscribes to selection events.
    /// Must be called after the dispatcher has registered all handlers.
    /// </summary>
    public void Initialize(Node parentNode)
    {
        _parentNode = parentNode;
        CreateSubViewport();

        // Subscribe to selection changes
        var selectionHandler = _dispatcher.GetHandler<SelectionHandler>();
        if (selectionHandler != null)
        {
            selectionHandler.SelectionChanged += OnSelectionChanged;
            _logger.Info("PreviewManager subscribed to SelectionChanged");
        }
        else
        {
            _logger.Warning("SelectionHandler not found — preview will not auto-trigger");
        }

        // Subscribe to game version changes — recreate provider when version changes
        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler != null)
        {
            projectHandler.GameVersionChanged += OnGameVersionChanged;
            _logger.Info("PreviewManager subscribed to GameVersionChanged");
        }
    }

    private void CreateSubViewport()
    {
        _subViewport = new SubViewport
        {
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            TransparentBg = false,
            OwnWorld3D = true,
        };

        _camera = new Camera3D
        {
            Far = 1000000f, // UE4 uses centimeters — meshes can be very large
        };

        _light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-45, 45, 0),
            ShadowEnabled = false,
            LightEnergy = 1.2f,
        };

        // Add a fill light from the opposite direction
        var fillLight = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-20, -135, 0),
            ShadowEnabled = false,
            LightEnergy = 0.4f,
        };

        _meshInstance = new MeshInstance3D();

        // Add an environment for ambient lighting
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.12f, 0.12f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.3f, 0.3f, 0.35f),
                AmbientLightEnergy = 0.5f,
            }
        };

        _subViewport.AddChild(_camera);
        _subViewport.AddChild(_light);
        _subViewport.AddChild(fillLight);
        _subViewport.AddChild(_meshInstance);
        _subViewport.AddChild(env);
        _parentNode!.AddChild(_subViewport);
        UpdateCameraTransform();

        _logger.Info("PreviewManager SubViewport created ({Width}x{Height})", ViewportWidth, ViewportHeight);
    }

    private void OnSelectionChanged(SelectionState state)
    {
        if (state.SelectedId == null) return;

        // Only handle file nodes from the project tree
        if (!state.SelectedId.StartsWith("file:")) return;

        var relativePath = state.SelectedId.Substring(5); // Strip "file:" prefix

        // Only preview .uasset files
        if (!relativePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return;

        _ = LoadPreviewAsync(relativePath);
    }

    private void OnGameVersionChanged(EGame newVersion)
    {
        _logger.Info("Game version changed to {Version}, disposing provider", newVersion);

        // Dispose the existing provider so it gets recreated with the new version
        DisposeProvider();

        // Re-preview the current selection if there is one
        var selectionHandler = _dispatcher.GetHandler<SelectionHandler>();
        var selectedId = selectionHandler?.CurrentState.SelectedId;
        if (selectedId != null && selectedId.StartsWith("file:"))
        {
            var relativePath = selectedId.Substring(5);
            if (relativePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            {
                _ = LoadPreviewAsync(relativePath);
            }
        }
    }

    private async Task LoadPreviewAsync(string relativePath)
    {
        using var activity = ActivitySource.StartActivity("LoadPreview");
        activity?.SetTag("preview.path", relativePath);

        try
        {
            // Notify frontend of loading state
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = true });

            // Ensure we have a CUE4Parse provider for the current project
            await EnsureProviderAsync();

            if (_fileProvider == null)
            {
                _logger.Warning("No file provider available for preview");
                _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
                return;
            }

            var assetName = Path.GetFileNameWithoutExtension(relativePath);

            // Resolve the correct game path by trying multiple formats.
            // CUE4Parse path indexing depends on the provider version and mount method.
            var gamePath = ResolveGamePath(relativePath);

            // LoadPackageObject expects path WITHOUT .uasset extension
            var loadPath = gamePath;
            if (loadPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            {
                loadPath = loadPath.Substring(0, loadPath.Length - 7);
            }

            _logger.Info("Loading preview: {LoadPath} (EGame={Game})",
                loadPath, _fileProvider.Versions.Game);

            // Try loading as each supported type via CUE4Parse.
            // LoadPackageObject throws if the asset can't be loaded as that type.
            var texture = await Task.Run(() =>
            {
                try { return _fileProvider.LoadPackageObject<UTexture2D>(loadPath); }
                catch (Exception ex)
                {
                    _logger.Warning("Not a UTexture2D: {Error}", ex.Message);
                    return null;
                }
            });
            if (texture != null)
            {
                // If texture loaded but PlatformData is empty, the EGame version is likely wrong.
                // Try common UE4 versions to find one that deserializes correctly.
                if (texture.PlatformData.SizeX == 0 && texture.PlatformData.SizeY == 0)
                {
                    _logger.Warning("Texture has empty PlatformData — trying version fallback...");
                    var fallbackResult = await TryVersionFallbackAsync(loadPath);
                    if (fallbackResult != null)
                    {
                        texture = fallbackResult;
                    }
                }

                await PreviewTextureAsync(texture, assetName);
                return;
            }

            var staticMesh = await Task.Run(() =>
            {
                try { return _fileProvider.LoadPackageObject<UStaticMesh>(loadPath); }
                catch (Exception ex)
                {
                    _logger.Debug("Not a UStaticMesh: {Error}", ex.Message);
                    return null;
                }
            });
            if (staticMesh != null)
            {
                await PreviewStaticMeshAsync(staticMesh, assetName);
                return;
            }

            var skelMesh = await Task.Run(() =>
            {
                try { return _fileProvider.LoadPackageObject<USkeletalMesh>(loadPath); }
                catch (Exception ex)
                {
                    _logger.Debug("Not a USkeletalMesh: {Error}", ex.Message);
                    return null;
                }
            });
            if (skelMesh != null)
            {
                await PreviewSkeletalMeshAsync(skelMesh, assetName);
                return;
            }

            _logger.Info("No previewable content found in: {Path}", relativePath);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load preview for: {Path}", relativePath);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
        }
    }

    private async Task PreviewTextureAsync(UTexture2D texture, string assetName)
    {
        _logger.Info("Previewing texture: {Name}", assetName);

        var image = await _textureExtractor.ExtractFromTexture2DAsync(texture);
        if (image == null)
        {
            _logger.Warning("Failed to extract texture: {Name}", assetName);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        var pngBytes = image.SavePngToBuffer();
        var base64 = Convert.ToBase64String(pngBytes);
        var dataUrl = $"data:image/png;base64,{base64}";

        var info = _textureExtractor.GetTextureInfo(texture);

        _dispatcher.Send(MessageTypes.Viewport, "preview", new
        {
            imageData = dataUrl,
            mode = "2d",
            contentType = "texture",
            assetName,
            textureInfo = info != null ? new
            {
                width = info.Width,
                height = info.Height,
                format = info.Format,
            } : null,
        });
    }

    private async Task PreviewStaticMeshAsync(UStaticMesh mesh, string assetName)
    {
        _logger.Info("Previewing static mesh: {Name}", assetName);

        var result = await _meshExtractor.ExtractStaticMeshAsync(mesh);
        if (result == null)
        {
            _logger.Warning("Failed to extract static mesh: {Name}", assetName);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        // Extract materials for each surface
        ExtractedMaterial?[]? materials = null;
        if (result.Sections.Length > 0)
        {
            try
            {
                materials = await _materialExtractor.ExtractMaterialsAsync(result.Sections, _fileProvider);
            }
            catch (Exception ex)
            {
                _logger.Warning("Material extraction failed, using defaults: {Error}", ex.Message);
            }
        }

        var meshInfo = _meshExtractor.GetStaticMeshInfo(mesh);
        RenderMeshAndSend(result.Mesh, assetName, meshInfo, materials);
    }

    private async Task PreviewSkeletalMeshAsync(USkeletalMesh mesh, string assetName)
    {
        _logger.Info("Previewing skeletal mesh: {Name}", assetName);

        var result = await _meshExtractor.ExtractSkeletalMeshAsync(mesh);
        if (result == null)
        {
            _logger.Warning("Failed to extract skeletal mesh: {Name}", assetName);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        ExtractedMaterial?[]? materials = null;
        if (result.Sections.Length > 0)
        {
            try
            {
                materials = await _materialExtractor.ExtractMaterialsAsync(result.Sections, _fileProvider);
            }
            catch (Exception ex)
            {
                _logger.Warning("Material extraction failed, using defaults: {Error}", ex.Message);
            }
        }

        RenderMeshAndSend(result.Mesh, assetName, null, materials);
    }

    private void RenderMeshAndSend(ArrayMesh arrayMesh, string assetName,
        MeshInfo? meshInfo, ExtractedMaterial?[]? materials = null)
    {
        if (_meshInstance == null || _subViewport == null || _camera == null) return;

        // Assign mesh to the instance
        _meshInstance.Mesh = arrayMesh;

        // Apply per-surface materials (extracted or default fallback)
        _logger.Info("ArrayMesh surfaces={SurfaceCount}", arrayMesh.GetSurfaceCount());
        for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
        {
            StandardMaterial3D surfaceMaterial;

            if (materials != null && i < materials.Length && materials[i] != null)
            {
                surfaceMaterial = materials[i]!.GodotMaterial;
                _logger.Debug("Surface {Index}: material '{Name}' ({TexCount} textures)",
                    i, materials[i]!.MaterialName ?? "unknown", materials[i]!.TextureCount);
            }
            else
            {
                surfaceMaterial = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.7f, 0.7f, 0.7f),
                    Roughness = 0.6f,
                    Metallic = 0.1f,
                };
                _logger.Debug("Surface {Index}: using default material", i);
            }

            surfaceMaterial.CullMode = _doubleSided
                ? BaseMaterial3D.CullModeEnum.Disabled
                : BaseMaterial3D.CullModeEnum.Back;

            arrayMesh.SurfaceSetMaterial(i, surfaceMaterial);
        }

        // Auto-frame the mesh
        FrameMesh(arrayMesh);
        _logger.Info("Camera: pos={Pos}, target={Target}, distance={Dist}",
            _camera.Position, _cameraTarget, _cameraDistance);

        // Request a single render frame from the SubViewport
        _subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        // Store info for the capture callback and wait 2 frames:
        // Frame 0: SubViewport renders the mesh
        // Frame 1: Capture the rendered result
        _pendingAssetName = assetName;
        _pendingMeshInfo = meshInfo;
        _captureCountdown = 2;
    }

    private string? _pendingAssetName;
    private MeshInfo? _pendingMeshInfo;

    /// <summary>
    /// Called by MainController on each process frame to check for pending captures.
    /// </summary>
    public void ProcessFrame()
    {
        if (_captureCountdown <= 0 || _subViewport == null) return;

        _captureCountdown--;
        if (_captureCountdown == 0)
        {
            CaptureAndSendMeshFrame(_pendingAssetName ?? "Unknown", _pendingMeshInfo);
        }
    }

    private void CaptureAndSendMeshFrame(string assetName, MeshInfo? meshInfo)
    {
        if (_subViewport == null) return;

        var viewportTexture = _subViewport.GetTexture();
        if (viewportTexture == null)
        {
            _logger.Warning("SubViewport texture is null");
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        var image = viewportTexture.GetImage();
        if (image == null)
        {
            _logger.Warning("Failed to get image from SubViewport");
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        var pngBytes = image.SavePngToBuffer();
        var base64 = Convert.ToBase64String(pngBytes);
        var dataUrl = $"data:image/png;base64,{base64}";

        _logger.Info("Captured mesh frame: {W}x{H}, PNG={PngSize} bytes, base64={B64Len} chars",
            image.GetWidth(), image.GetHeight(), pngBytes.Length, base64.Length);

        _dispatcher.Send(MessageTypes.Viewport, "preview", new
        {
            imageData = dataUrl,
            mode = "3d",
            contentType = "mesh",
            assetName,
            meshInfo = meshInfo != null ? new
            {
                vertexCount = meshInfo.VertexCount,
                triangleCount = meshInfo.TriangleCount,
                lodCount = meshInfo.LodCount,
            } : null,
        });
    }

    // -----------------------------------------------------------------
    // Camera Controls
    // -----------------------------------------------------------------

    private void FrameMesh(ArrayMesh mesh)
    {
        var aabb = mesh.GetAabb();
        _logger.Info("Mesh AABB: center={Center}, size={Size}, length={Len}",
            aabb.GetCenter(), aabb.Size, aabb.Size.Length());
        _cameraTarget = aabb.GetCenter();
        _cameraDistance = aabb.Size.Length() * 1.5f;
        if (_cameraDistance < 0.5f) _cameraDistance = 3f;

        // Reset angles to default viewing angle
        _cameraYaw = 45f;
        _cameraPitch = -30f;

        UpdateCameraTransform();
    }

    /// <summary>
    /// Orbits the camera by the given screen-space deltas (in pixels).
    /// </summary>
    public void HandleCameraOrbit(float dx, float dy)
    {
        _cameraYaw += dx * 0.3f;
        _cameraPitch = Mathf.Clamp(_cameraPitch + dy * 0.3f, -89f, 89f);
        UpdateCameraTransform();
        RequestCapture();
    }

    /// <summary>
    /// Zooms the camera by the given delta (positive = zoom in).
    /// </summary>
    public void HandleCameraZoom(float delta)
    {
        _cameraDistance = Mathf.Max(0.1f, _cameraDistance * (1f - delta * 0.1f));
        UpdateCameraTransform();
        RequestCapture();
    }

    /// <summary>
    /// Resets the camera to the default position framing the current mesh.
    /// </summary>
    public void HandleCameraReset()
    {
        if (_meshInstance?.Mesh is ArrayMesh arrayMesh)
        {
            FrameMesh(arrayMesh);
        }
        else
        {
            _cameraDistance = 3f;
            _cameraYaw = 45f;
            _cameraPitch = -30f;
            _cameraTarget = Vector3.Zero;
            UpdateCameraTransform();
        }
        RequestCapture();
    }

    /// <summary>
    /// Toggles double-sided (backface culling disabled) rendering for the current mesh.
    /// </summary>
    public void HandleSetDoubleSided(bool enabled)
    {
        _doubleSided = enabled;

        // Update materials on the current mesh in-place
        if (_meshInstance?.Mesh is ArrayMesh arrayMesh)
        {
            var cullMode = _doubleSided
                ? BaseMaterial3D.CullModeEnum.Disabled
                : BaseMaterial3D.CullModeEnum.Back;

            for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
            {
                if (arrayMesh.SurfaceGetMaterial(i) is StandardMaterial3D mat)
                {
                    mat.CullMode = cullMode;
                }
            }

            RequestCapture();
        }
    }

    private void UpdateCameraTransform()
    {
        if (_camera == null) return;

        var yawRad = Mathf.DegToRad(_cameraYaw);
        var pitchRad = Mathf.DegToRad(_cameraPitch);

        var offset = new Vector3(
            _cameraDistance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            _cameraDistance * -Mathf.Sin(pitchRad),
            _cameraDistance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
        );

        _camera.Position = _cameraTarget + offset;
        _camera.LookAt(_cameraTarget);
    }

    private void RequestCapture()
    {
        if (_subViewport == null || _meshInstance?.Mesh == null) return;

        _subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _captureCountdown = 2;
        // pendingAssetName/pendingMeshInfo remain from the last render
    }

    // -----------------------------------------------------------------
    // EGame Version Fallback
    // -----------------------------------------------------------------

    /// <summary>
    /// EGame versions to try when the current version produces empty PlatformData.
    /// Ordered from most to least common. The key boundary is UE4.20 where
    /// the skip offset in DeserializeCookedPlatformData changes from int32 to int64.
    /// </summary>
    private static readonly EGame[] VersionFallbacks =
    {
        EGame.GAME_UE4_17,  // Pre-4.20 (4-byte skip offsets)
        EGame.GAME_UE4_14,  // Older UE4
        EGame.GAME_UE4_22,  // UE4.20-4.22
        EGame.GAME_UE4_25,  // UE4.23-4.25
        EGame.GAME_UE4_27,  // UE4.26-4.27
        EGame.GAME_UE5_0,   // UE5.0
        EGame.GAME_UE5_3,   // UE5.3+
    };

    /// <summary>
    /// Tries loading a texture with different EGame versions when the current
    /// version produces empty PlatformData. Returns the texture if a version
    /// works, or null if none do.
    /// </summary>
    private async Task<UTexture2D?> TryVersionFallbackAsync(string loadPath)
    {
        if (_fileProvider == null) return null;

        var originalVersion = _fileProvider.Versions.Game;

        foreach (var version in VersionFallbacks)
        {
            if (version == originalVersion) continue; // Already tried

            _logger.Info("Trying EGame fallback: {Version}", version);
            _fileProvider.Versions.Game = version;

            try
            {
                var texture = await Task.Run(() =>
                {
                    try { return _fileProvider.LoadPackageObject<UTexture2D>(loadPath); }
                    catch { return null; }
                });

                if (texture != null && texture.PlatformData.SizeX > 0 && texture.PlatformData.SizeY > 0)
                {
                    _logger.Info("Version fallback succeeded with {Version}: {W}x{H} {Fmt}",
                        version, texture.PlatformData.SizeX, texture.PlatformData.SizeY,
                        texture.PlatformData.PixelFormat);

                    // Update the version state so future loads use this version
                    _currentVersion = version;
                    var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
                    if (projectHandler != null)
                    {
                        projectHandler.SetGameVersionFromImport(
                            _projectPath!, version.ToString());
                        _logger.Info("Auto-corrected game version to {Version}", version);
                    }

                    return texture;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Fallback {Version} failed: {Error}", version, ex.Message);
            }
        }

        // None worked — restore original version
        _fileProvider.Versions.Game = originalVersion;
        _logger.Warning("No EGame version produced valid texture data. " +
            "Try selecting the correct game from the version dropdown.");
        return null;
    }

    // -----------------------------------------------------------------
    // CUE4Parse Path Resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Resolves the correct game path for CUE4Parse by trying multiple formats.
    /// Logs diagnostic info when the path isn't found.
    /// </summary>
    private string ResolveGamePath(string relativePath)
    {
        // Normalize separators to forward slashes
        var normalizedPath = relativePath.Replace('\\', '/');

        // Prepare both with and without .uasset extension
        var withExt = normalizedPath;
        var withoutExt = normalizedPath;
        if (normalizedPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
        {
            withoutExt = normalizedPath.Substring(0, normalizedPath.Length - 7);
        }
        else
        {
            withExt = normalizedPath + ".uasset";
        }

        var projectDirName = Path.GetFileName(
            _projectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Try multiple path formats — CUE4Parse behavior varies between
        // PAK-mounted (keys without extension) and loose-file (keys with extension)
        string[] candidates =
        {
            projectDirName + "/" + withoutExt,  // Parent-relative, no extension (PAK style)
            withoutExt,                         // Dir-relative, no extension
            projectDirName + "/" + withExt,      // Parent-relative, with extension (loose file style)
            withExt,                            // Dir-relative, with extension
        };

        foreach (var candidate in candidates)
        {
            if (_fileProvider!.Files.ContainsKey(candidate))
            {
                _logger.Info("Resolved game path: {Path}", candidate);
                return candidate;
            }
        }

        // None matched — log diagnostics
        _logger.Warning("Game path not found in provider index (tried {Count} formats, provider has {FileCount} files)",
            candidates.Length, _fileProvider!.Files.Count);
        foreach (var candidate in candidates)
        {
            _logger.Warning("  Tried: {Path}", candidate);
        }

        // Log sample keys so we can see the actual format
        var sampleKeys = _fileProvider.Files.Keys.Take(5).ToArray();
        if (sampleKeys.Length > 0)
        {
            _logger.Info("Sample provider keys:");
            foreach (var key in sampleKeys)
            {
                _logger.Info("  {Key}", key);
            }
        }

        // Try to find the file by name alone (with or without extension)
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var matchingKeys = _fileProvider.Files.Keys
            .Where(k => k.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)
                     || k.EndsWith("/" + fileName + ".uasset", StringComparison.OrdinalIgnoreCase)
                     || k.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToArray();
        if (matchingKeys.Length > 0)
        {
            _logger.Info("Found matching paths by filename '{FileName}':", fileName);
            foreach (var key in matchingKeys)
            {
                _logger.Info("  {Key}", key);
            }
            // Use the first match
            _logger.Info("Using first match: {Path}", matchingKeys[0]);
            return matchingKeys[0];
        }

        // Fall back to the first candidate (will likely fail at load time)
        _logger.Warning("No matching paths found for '{FileName}' — using best guess: {Path}", fileName, candidates[0]);
        return candidates[0];
    }

    // -----------------------------------------------------------------
    // CUE4Parse Provider Management
    // -----------------------------------------------------------------

    private async Task EnsureProviderAsync()
    {
        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        var currentProject = projectHandler?.CurrentProject;

        if (currentProject == null)
        {
            _logger.Debug("No project open — cannot create file provider");
            DisposeProvider();
            return;
        }

        var gameVersion = projectHandler!.EffectiveGameVersion;

        // If the project path and version haven't changed, reuse the existing provider
        if (_fileProvider != null && _projectPath == currentProject.Path && _currentVersion == gameVersion)
        {
            return;
        }

        // Dispose old provider and create new one
        DisposeProvider();
        _projectPath = currentProject.Path;
        _currentVersion = gameVersion;

        _logger.Info("Creating CUE4Parse provider for project: {Path}, version: {Version}", _projectPath, gameVersion);

        CompressionInitializerFactory.EnsureInitialized(_logger);

        _fileProvider = new DefaultFileProvider(
            _projectPath,
            SearchOption.AllDirectories,
            versions: new VersionContainer(gameVersion),
            pathComparer: StringComparer.OrdinalIgnoreCase
        );

        _fileProvider.Initialize();
        await Task.Run(() => _fileProvider.Mount());

        _logger.Info("File provider ready: {FileCount} files, version: {Version}",
            _fileProvider.Files.Count, gameVersion);
    }

    private void DisposeProvider()
    {
        _fileProvider?.Dispose();
        _fileProvider = null;
        _projectPath = null;
    }

    /// <summary>
    /// Clears the current preview and disposes the file provider.
    /// Called when the project is closed.
    /// </summary>
    public void ClearPreview()
    {
        if (_meshInstance != null)
        {
            _meshInstance.Mesh = null;
        }
        DisposeProvider();
        _dispatcher.Send(MessageTypes.Viewport, "cleared", new { });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from selection changes
        var selectionHandler = _dispatcher.GetHandler<SelectionHandler>();
        if (selectionHandler != null)
        {
            selectionHandler.SelectionChanged -= OnSelectionChanged;
        }

        // Unsubscribe from game version changes
        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler != null)
        {
            projectHandler.GameVersionChanged -= OnGameVersionChanged;
        }

        DisposeProvider();

        // Clean up SubViewport scene tree
        _subViewport?.QueueFree();
        _subViewport = null;
        _camera = null;
        _meshInstance = null;
        _light = null;
    }
}
