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

    private DefaultFileProvider? _fileProvider;
    private string? _projectPath;

    // SubViewport scene tree for 3D rendering
    private SubViewport? _subViewport;
    private Camera3D? _camera;
    private MeshInstance3D? _meshInstance;
    private DirectionalLight3D? _light;
    private Node? _parentNode;
    private bool _pendingCapture;

    // Orbital camera state
    private float _cameraDistance = 3f;
    private float _cameraYaw = 45f;    // degrees
    private float _cameraPitch = -30f;  // degrees
    private Vector3 _cameraTarget = Vector3.Zero;

    private bool _disposed;

    private const int ViewportWidth = 1024;
    private const int ViewportHeight = 768;

    public PreviewManager(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _textureExtractor = new TextureExtractor(logger);
        _meshExtractor = new MeshExtractor(logger);
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
    }

    private void CreateSubViewport()
    {
        _subViewport = new SubViewport
        {
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            TransparentBg = true,
            OwnWorld3D = true,
        };

        _camera = new Camera3D();

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

            // Convert file path to CUE4Parse game path.
            // CUE4Parse registers paths relative to the PARENT of the mounted directory,
            // so we need to prepend the project directory name.
            var projectDirName = Path.GetFileName(
                _projectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var gamePath = projectDirName + "/" + relativePath;
            if (gamePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            {
                gamePath = gamePath.Substring(0, gamePath.Length - 7);
            }

            var assetName = Path.GetFileNameWithoutExtension(relativePath);

            _logger.Info("Loading preview for: gamePath={GamePath}, relativePath={RelPath}, projectPath={ProjPath}",
                gamePath, relativePath, _projectPath ?? "(null)");

            // Check if the game path exists in the provider's file index
            var fileFound = _fileProvider.Files.ContainsKey(gamePath);
            _logger.Info("Game path in provider index: {Found} (provider has {Count} files)",
                fileFound, _fileProvider.Files.Count);

            if (!fileFound)
            {
                // Try to find similar paths to diagnose path format mismatch
                var fileName = Path.GetFileNameWithoutExtension(relativePath);
                var possibleKeys = _fileProvider.Files.Keys
                    .Where(k => k.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .ToArray();
                if (possibleKeys.Length > 0)
                {
                    _logger.Info("Possible matching paths in provider:");
                    foreach (var key in possibleKeys)
                    {
                        _logger.Info("  {Key}", key);
                    }
                }
            }

            // Try loading as each supported type via CUE4Parse.
            // LoadPackageObject throws if the asset can't be loaded as that type.
            var texture = await Task.Run(() =>
            {
                try { return _fileProvider.LoadPackageObject<UTexture2D>(gamePath); }
                catch (Exception ex)
                {
                    _logger.Debug("Not a UTexture2D: {Error}", ex.Message);
                    return null;
                }
            });
            if (texture != null)
            {
                await PreviewTextureAsync(texture, assetName);
                return;
            }

            var staticMesh = await Task.Run(() =>
            {
                try { return _fileProvider.LoadPackageObject<UStaticMesh>(gamePath); }
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
                try { return _fileProvider.LoadPackageObject<USkeletalMesh>(gamePath); }
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

        var arrayMesh = await _meshExtractor.ExtractStaticMeshAsync(mesh);
        if (arrayMesh == null)
        {
            _logger.Warning("Failed to extract static mesh: {Name}", assetName);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        var meshInfo = _meshExtractor.GetStaticMeshInfo(mesh);
        RenderMeshAndSend(arrayMesh, assetName, meshInfo);
    }

    private async Task PreviewSkeletalMeshAsync(USkeletalMesh mesh, string assetName)
    {
        _logger.Info("Previewing skeletal mesh: {Name}", assetName);

        var arrayMesh = await _meshExtractor.ExtractSkeletalMeshAsync(mesh);
        if (arrayMesh == null)
        {
            _logger.Warning("Failed to extract skeletal mesh: {Name}", assetName);
            _dispatcher.Send(MessageTypes.Viewport, "loading", new { loading = false });
            return;
        }

        RenderMeshAndSend(arrayMesh, assetName, null);
    }

    private void RenderMeshAndSend(ArrayMesh arrayMesh, string assetName, MeshInfo? meshInfo)
    {
        if (_meshInstance == null || _subViewport == null || _camera == null) return;

        // Assign mesh to the instance
        _meshInstance.Mesh = arrayMesh;

        // Apply a default material so the mesh is visible
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.7f, 0.7f),
            Roughness = 0.6f,
            Metallic = 0.1f,
        };
        for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
        {
            arrayMesh.SurfaceSetMaterial(i, material);
        }

        // Auto-frame the mesh
        FrameMesh(arrayMesh);

        // Request a render and capture the frame
        // We need to wait for the next rendered frame
        _subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        // Store info for the capture callback
        _pendingCapture = true;
        _pendingAssetName = assetName;
        _pendingMeshInfo = meshInfo;

        // Schedule capture after render completes (use deferred call)
        _subViewport.CallDeferred("_notify_capture_ready");
    }

    private string? _pendingAssetName;
    private MeshInfo? _pendingMeshInfo;

    /// <summary>
    /// Called by MainController on each process frame to check for pending captures.
    /// </summary>
    public void ProcessFrame()
    {
        if (!_pendingCapture || _subViewport == null) return;
        _pendingCapture = false;

        CaptureAndSendMeshFrame(_pendingAssetName ?? "Unknown", _pendingMeshInfo);
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
        _pendingCapture = true;
        // pendingAssetName/pendingMeshInfo remain from the last render
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

        // If the project path hasn't changed, reuse the existing provider
        if (_fileProvider != null && _projectPath == currentProject.Path)
        {
            return;
        }

        // Dispose old provider and create new one
        DisposeProvider();
        _projectPath = currentProject.Path;

        _logger.Info("Creating CUE4Parse provider for project: {Path}", _projectPath);

        CompressionInitializerFactory.EnsureInitialized(_logger);

        var ueVersion = DetectUeVersion(_projectPath);
        _logger.Info("Detected UE version: {Version}", ueVersion);

        _fileProvider = new DefaultFileProvider(
            _projectPath,
            SearchOption.AllDirectories,
            versions: new VersionContainer(ueVersion),
            pathComparer: StringComparer.OrdinalIgnoreCase
        );

        _fileProvider.Initialize();
        await Task.Run(() => _fileProvider.Mount());

        _logger.Info("File provider ready: {FileCount} files", _fileProvider.Files.Count);
    }

    /// <summary>
    /// Detects UE version by reading the legacy file version from the first .uasset found.
    /// UE4 packages have legacy version >= -7, UE5 packages have <= -8.
    /// </summary>
    private EGame DetectUeVersion(string projectPath)
    {
        try
        {
            // Find the first .uasset file in the project
            var uassetFiles = Directory.EnumerateFiles(projectPath, "*.uasset", SearchOption.AllDirectories);
            foreach (var filePath in uassetFiles)
            {
                using var fs = File.OpenRead(filePath);
                if (fs.Length < 8) continue;

                var header = new byte[8];
                if (fs.Read(header, 0, 8) < 8) continue;

                // Check magic number (little-endian 0x9E2A83C1)
                uint magic = BitConverter.ToUInt32(header, 0);
                if (magic != 0x9E2A83C1) continue;

                // Read legacy file version
                int legacyVersion = BitConverter.ToInt32(header, 4);
                _logger.Debug("Package {Path}: magic=0x{Magic:X8}, legacyVersion={Version}",
                    Path.GetFileName(filePath), magic, legacyVersion);

                // UE5 packages use legacy version <= -8
                if (legacyVersion <= -8)
                {
                    return EGame.GAME_UE5_3;
                }

                // UE4 packages use legacy version >= -7
                return EGame.GAME_UE4_27;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to detect UE version, defaulting to UE4: {Error}", ex.Message);
        }

        return EGame.GAME_UE4_27;
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

        DisposeProvider();

        // Clean up SubViewport scene tree
        _subViewport?.QueueFree();
        _subViewport = null;
        _camera = null;
        _meshInstance = null;
        _light = null;
    }
}
