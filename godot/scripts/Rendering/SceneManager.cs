// Scene Manager - Multi-Mesh Level Renderer
//
// Loads .umap level files and renders all mesh actors in a single SubViewport.
// Each actor gets its own MeshInstance3D positioned at its level transform.
// Provides progressive loading with per-batch frame capture.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Versions;
using Godot;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Assets.Compression;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Rendering;

/// <summary>
/// Manages scene-level rendering of .umap files with multiple mesh actors.
/// Owns its own SubViewport, independent from PreviewManager.
/// </summary>
public sealed class SceneManager : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly MeshExtractor _meshExtractor;
    private readonly MaterialExtractor _materialExtractor;
    private readonly TextureExtractor _textureExtractor;
    private readonly LevelExtractor _levelExtractor;

    // CUE4Parse provider (independent from PreviewManager)
    private DefaultFileProvider? _fileProvider;
    private string? _projectPath;
    private EGame _currentVersion;

    // SubViewport scene tree
    private SubViewport? _subViewport;
    private Camera3D? _camera;
    private DirectionalLight3D? _light;
    private DirectionalLight3D? _fillLight;
    private WorldEnvironment? _worldEnvironment;
    private Node3D? _sceneRoot;
    private Node? _parentNode;

    // Actor mesh tracking
    private readonly Dictionary<string, MeshInstance3D> _actorMeshes = new();

    // Camera state (same orbital model as PreviewManager)
    private float _cameraDistance = 1000f;
    private float _cameraYaw = 45f;
    private float _cameraPitch = -30f;
    private Vector3 _cameraTarget = Vector3.Zero;

    // Rendering state
    private bool _doubleSided = true;
    private string _renderMode = "shaded";
    private int _captureCountdown;
    private string? _pendingLevelName;

    // Scene state
    private LevelExtractionResult? _currentLevel;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    // Constants
    private const int ViewportWidth = 1024;
    private const int ViewportHeight = 768;
    private const int BatchSize = 10;
    private const int MaxMeshActors = 500;

    /// <summary>
    /// Whether scene mode is currently active (used by ViewportHandler for camera routing).
    /// </summary>
    public bool IsActive { get; private set; }

    public SceneManager(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _textureExtractor = new TextureExtractor(logger);
        _meshExtractor = new MeshExtractor(logger);
        _materialExtractor = new MaterialExtractor(logger, _textureExtractor);
        _levelExtractor = new LevelExtractor(logger);
    }

    /// <summary>
    /// Creates the SubViewport and subscribes to selection events.
    /// Must be called after dispatcher has registered all handlers.
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
            _logger.Info("SceneManager subscribed to SelectionChanged");
        }

        // Subscribe to game version changes
        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler != null)
        {
            projectHandler.GameVersionChanged += OnGameVersionChanged;
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
            Far = 1000000f,
        };

        _light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-45, 45, 0),
            ShadowEnabled = false,
            LightEnergy = 1.2f,
        };

        _fillLight = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-20, -135, 0),
            ShadowEnabled = false,
            LightEnergy = 0.4f,
        };

        _worldEnvironment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.12f, 0.12f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.3f, 0.3f, 0.35f),
                AmbientLightEnergy = 0.5f,
                TonemapMode = Godot.Environment.ToneMapper.Aces,
            }
        };

        _sceneRoot = new Node3D { Name = "SceneRoot" };

        _subViewport.AddChild(_camera);
        _subViewport.AddChild(_light);
        _subViewport.AddChild(_fillLight);
        _subViewport.AddChild(_worldEnvironment);
        _subViewport.AddChild(_sceneRoot);
        _parentNode!.AddChild(_subViewport);

        UpdateCameraTransform();
        _logger.Info("SceneManager SubViewport created ({W}x{H})", ViewportWidth, ViewportHeight);
    }

    // -----------------------------------------------------------------
    // Selection Handling
    // -----------------------------------------------------------------

    private void OnSelectionChanged(SelectionState state)
    {
        if (state.SelectedId == null) return;
        if (!state.SelectedId.StartsWith("file:")) return;

        var relativePath = state.SelectedId.Substring(5);

        if (relativePath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadLevelAsync(relativePath);
        }
        else if (IsActive)
        {
            // Switching away from .umap — exit scene mode
            ClearScene();
        }
    }

    private void OnGameVersionChanged(EGame newVersion)
    {
        _logger.Info("Scene: game version changed to {Version}, disposing provider", newVersion);
        DisposeProvider();
    }

    // -----------------------------------------------------------------
    // Level Loading
    // -----------------------------------------------------------------

    /// <summary>
    /// Loads a .umap level and progressively renders all mesh actors.
    /// </summary>
    public async Task LoadLevelAsync(string relativePath)
    {
        // Cancel any in-progress load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        ClearSceneNodes();
        IsActive = true;

        _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = true });

        try
        {
            // Resolve the full path to the .umap file
            var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
            if (projectHandler?.CurrentProject == null)
            {
                _logger.Warning("No project open — cannot load level");
                _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = false });
                IsActive = false;
                return;
            }

            var fullPath = Path.Combine(projectHandler.CurrentProject.Path, relativePath);
            if (!File.Exists(fullPath))
            {
                _logger.Warning("Level file not found: {Path}", fullPath);
                _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = false });
                IsActive = false;
                return;
            }

            // Map EGame to UAssetAPI EngineVersion
            var engineVersion = MapEGameToEngineVersion(projectHandler.EffectiveGameVersion.ToString());

            // Phase 1: Extract actor metadata from .umap via UAssetAPI
            var extractionProgress = new Progress<(int loaded, int total)>(p =>
            {
                _dispatcher.Send(MessageTypes.Scene, "extractionProgress",
                    new { loaded = p.loaded, total = p.total });
            });

            _logger.Info("Extracting actors from: {Path}", fullPath);
            var result = await _levelExtractor.ExtractLevelAsync(
                fullPath, engineVersion, extractionProgress, ct);

            if (result == null)
            {
                _logger.Warning("Failed to extract level: {Path}", fullPath);
                _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = false });
                IsActive = false;
                return;
            }

            ct.ThrowIfCancellationRequested();
            _currentLevel = result;

            // Send actor list to frontend for the outliner
            var sceneActors = result.Actors.Select(a => new SceneActor(
                a.Id, a.Name, a.ClassName, a.MeshPath,
                new[] { a.Transform.Origin.X, a.Transform.Origin.Y, a.Transform.Origin.Z },
                a.MeshPath != null, false
            )).ToArray();

            var meshActorCount = sceneActors.Count(a => a.HasMesh);
            _dispatcher.Send(MessageTypes.Scene, "actorList", new
            {
                levelName = result.LevelName,
                actors = sceneActors,
                totalCount = result.Actors.Length,
                meshCount = meshActorCount,
            });

            // Phase 2: Progressive mesh loading via CUE4Parse
            var meshActors = result.Actors
                .Where(a => a.MeshPath != null)
                .Take(MaxMeshActors)
                .ToArray();

            if (meshActors.Length > 0)
            {
                // Log sample mesh paths for debugging
                var samplePaths = meshActors.Take(5).Select(a => a.MeshPath).ToArray();
                _logger.Info("Scene: {Count} mesh actors to load. Sample paths: {Paths}",
                    meshActors.Length, string.Join(", ", samplePaths));

                await EnsureProviderAsync();

                if (_fileProvider == null)
                {
                    _logger.Warning("No CUE4Parse provider — cannot load meshes");
                    _dispatcher.Send(MessageTypes.Scene, "loaded", new
                    {
                        levelName = result.LevelName,
                        actorCount = 0,
                    });
                    return;
                }

                // Log sample provider keys for comparison
                var sampleKeys = _fileProvider.Files.Keys.Take(5).ToArray();
                _logger.Info("Scene: Provider has {Count} files. Sample keys: {Keys}",
                    _fileProvider.Files.Count, string.Join(", ", sampleKeys));

                _pendingLevelName = result.LevelName;

                for (int i = 0; i < meshActors.Length; i += BatchSize)
                {
                    ct.ThrowIfCancellationRequested();

                    var batch = meshActors.Skip(i).Take(BatchSize);
                    foreach (var actor in batch)
                    {
                        await LoadActorMeshAsync(actor, ct);
                    }

                    var loaded = Math.Min(i + BatchSize, meshActors.Length);
                    _dispatcher.Send(MessageTypes.Scene, "loadProgress", new
                    {
                        loaded,
                        total = meshActors.Length,
                    });

                    // Capture a frame after each batch
                    RequestCapture();

                    // Yield to allow frame processing
                    await Task.Delay(1, ct);
                }
            }

            // Auto-frame the entire scene
            FrameScene();

            if (meshActors.Length > MaxMeshActors)
            {
                _logger.Warning("Level has {Total} mesh actors, limited to {Max}",
                    result.Actors.Count(a => a.MeshPath != null), MaxMeshActors);
            }

            _dispatcher.Send(MessageTypes.Scene, "loaded", new
            {
                levelName = result.LevelName,
                actorCount = _actorMeshes.Count,
            });

            _logger.Info("Scene loaded: {Name} with {Count} meshes", result.LevelName, _actorMeshes.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Level load cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load level: {Path}", relativePath);
            _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = false });
        }
    }

    private async Task LoadActorMeshAsync(ActorData actor, CancellationToken ct)
    {
        if (actor.MeshPath == null || _fileProvider == null) return;

        try
        {
            // Resolve the mesh game path to a provider-compatible load path
            var resolvedPath = ResolveGamePathForMesh(actor.MeshPath);
            if (resolvedPath == null)
            {
                _logger.Debug("Could not resolve mesh path for {Name}: {Path}", actor.Name, actor.MeshPath);
                return;
            }

            // LoadPackageObject expects path WITHOUT .uasset extension
            var loadPath = resolvedPath;
            if (loadPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                loadPath = loadPath[..^7];
            if (loadPath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                loadPath = loadPath[..^5];

            // Try to load as static mesh first, then skeletal mesh
            MeshExtractionResult? extractionResult = null;

            var staticMesh = await Task.Run(() =>
            {
                try { return _fileProvider.LoadPackageObject<UStaticMesh>(loadPath); }
                catch (Exception ex)
                {
                    _logger.Debug("StaticMesh load failed for {Name}: {Error}", actor.Name, ex.Message);
                    return null;
                }
            }, ct);

            if (staticMesh != null)
            {
                extractionResult = await _meshExtractor.ExtractStaticMeshAsync(staticMesh);
            }
            else
            {
                var skelMesh = await Task.Run(() =>
                {
                    try { return _fileProvider.LoadPackageObject<USkeletalMesh>(loadPath); }
                    catch (Exception ex)
                    {
                        _logger.Debug("SkeletalMesh load failed for {Name}: {Error}", actor.Name, ex.Message);
                        return null;
                    }
                }, ct);

                if (skelMesh != null)
                {
                    extractionResult = await _meshExtractor.ExtractSkeletalMeshAsync(skelMesh);
                }
            }

            if (extractionResult == null)
            {
                _logger.Debug("Could not load mesh for actor {Name}: {Path} (resolved: {Resolved}, loadPath: {Load})",
                    actor.Name, actor.MeshPath, resolvedPath, loadPath);
                return;
            }

            // Extract materials
            ExtractedMaterial?[]? materials = null;
            if (extractionResult.Sections.Length > 0)
            {
                try
                {
                    materials = await _materialExtractor.ExtractMaterialsAsync(
                        extractionResult.Sections, _fileProvider);
                }
                catch (Exception ex)
                {
                    _logger.Debug("Material extraction failed for {Name}: {Error}", actor.Name, ex.Message);
                }
            }

            // Create MeshInstance3D at actor's transform
            var meshInstance = new MeshInstance3D
            {
                Mesh = extractionResult.Mesh,
                Transform = actor.Transform,
                Name = actor.Name,
            };

            // Apply materials
            for (int i = 0; i < extractionResult.Mesh.GetSurfaceCount(); i++)
            {
                StandardMaterial3D surfaceMaterial;
                if (materials != null && i < materials.Length && materials[i] != null)
                {
                    surfaceMaterial = materials[i]!.GodotMaterial;
                }
                else
                {
                    surfaceMaterial = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.7f, 0.7f, 0.7f),
                        Roughness = 0.6f,
                        Metallic = 0.1f,
                    };
                }

                surfaceMaterial.CullMode = _doubleSided
                    ? BaseMaterial3D.CullModeEnum.Disabled
                    : BaseMaterial3D.CullModeEnum.Back;

                extractionResult.Mesh.SurfaceSetMaterial(i, surfaceMaterial);
            }

            _sceneRoot!.AddChild(meshInstance);
            _actorMeshes[actor.Id] = meshInstance;
        }
        catch (Exception ex)
        {
            _logger.Debug("Failed to load actor mesh {Name}: {Error}", actor.Name, ex.Message);
        }
    }

    // -----------------------------------------------------------------
    // Camera Controls (same orbital math as PreviewManager)
    // -----------------------------------------------------------------

    private void FrameScene()
    {
        if (_actorMeshes.Count == 0) return;

        Aabb combinedAabb = default;
        bool first = true;

        foreach (var meshInst in _actorMeshes.Values)
        {
            if (meshInst.Mesh == null) continue;
            var meshAabb = meshInst.GetAabb();
            var worldAabb = meshInst.Transform * meshAabb;

            if (first) { combinedAabb = worldAabb; first = false; }
            else combinedAabb = combinedAabb.Merge(worldAabb);
        }

        if (first) return; // No valid meshes

        _cameraTarget = combinedAabb.GetCenter();
        _cameraDistance = combinedAabb.Size.Length() * 1.5f;
        if (_cameraDistance < 1f) _cameraDistance = 1000f;

        _cameraYaw = 45f;
        _cameraPitch = -30f;
        UpdateCameraTransform();
        RequestCapture();
        PushCameraState();
    }

    public void HandleCameraOrbit(float dx, float dy)
    {
        _cameraYaw += dx * 0.3f;
        _cameraPitch = Mathf.Clamp(_cameraPitch + dy * 0.3f, -89f, 89f);
        UpdateCameraTransform();
        RequestCapture();
        PushCameraState();
    }

    public void HandleCameraPan(float dx, float dy)
    {
        if (_camera == null) return;
        var right = _camera.GlobalTransform.Basis.X;
        var up = _camera.GlobalTransform.Basis.Y;
        var sensitivity = _cameraDistance * 0.002f;
        _cameraTarget -= right * dx * sensitivity;
        _cameraTarget += up * dy * sensitivity;
        UpdateCameraTransform();
        RequestCapture();
    }

    public void HandleCameraZoom(float delta)
    {
        _cameraDistance = Mathf.Max(0.1f, _cameraDistance * (1f - delta * 0.1f));
        UpdateCameraTransform();
        RequestCapture();
    }

    public void HandleCameraReset()
    {
        FrameScene();
    }

    public void HandleSetCameraView(float yaw, float pitch)
    {
        _cameraYaw = yaw;
        _cameraPitch = Mathf.Clamp(pitch, -89f, 89f);
        UpdateCameraTransform();
        RequestCapture();
        PushCameraState();
    }

    public void HandleSetDoubleSided(bool enabled)
    {
        _doubleSided = enabled;
        var cullMode = _doubleSided
            ? BaseMaterial3D.CullModeEnum.Disabled
            : BaseMaterial3D.CullModeEnum.Back;

        foreach (var meshInst in _actorMeshes.Values)
        {
            if (meshInst.Mesh is not ArrayMesh arrayMesh) continue;
            for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
            {
                if (arrayMesh.SurfaceGetMaterial(i) is StandardMaterial3D mat)
                {
                    mat.CullMode = cullMode;
                }
            }
        }
        RequestCapture();
    }

    public void HandleSetRenderMode(string mode)
    {
        _renderMode = mode;
        if (_subViewport != null)
        {
            _subViewport.DebugDraw = mode switch
            {
                "shadeless" => Viewport.DebugDrawEnum.Unshaded,
                "wireframe" => Viewport.DebugDrawEnum.Wireframe,
                _ => Viewport.DebugDrawEnum.Disabled,
            };
        }
        RequestCapture();
    }

    public void HandleSetTimeOfDay(float hours)
    {
        // Simplified time-of-day for scene mode — just adjust main light elevation
        if (_light == null) return;
        var clampedHours = Mathf.Clamp(hours, 0f, 24f);
        float elevation = 0;
        if (clampedHours >= 6f && clampedHours <= 18f)
        {
            float t = (clampedHours - 6f) / 12f;
            elevation = Mathf.Sin(t * Mathf.Pi) * 80f;
        }
        else
        {
            elevation = -10f;
        }
        _light.RotationDegrees = new Vector3(-elevation, 45f, 0f);
        RequestCapture();
    }

    /// <summary>
    /// Focus the camera on a specific actor.
    /// </summary>
    public void SelectActor(string actorId)
    {
        if (_actorMeshes.TryGetValue(actorId, out var meshInst))
        {
            var aabb = meshInst.GetAabb();
            var worldAabb = meshInst.Transform * aabb;
            _cameraTarget = worldAabb.GetCenter();
            _cameraDistance = Math.Max(worldAabb.Size.Length() * 2f, 100f);
            UpdateCameraTransform();
            RequestCapture();
            PushCameraState();
        }

        _dispatcher.Send(MessageTypes.Scene, "actorSelected", new { actorId });
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

    private void PushCameraState()
    {
        _dispatcher.Send(MessageTypes.Viewport, "cameraState", new
        {
            yaw = _cameraYaw,
            pitch = _cameraPitch,
        });
    }

    // -----------------------------------------------------------------
    // Frame Capture
    // -----------------------------------------------------------------

    private void RequestCapture()
    {
        if (_subViewport == null || !IsActive) return;
        _subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _captureCountdown = 2;
    }

    /// <summary>
    /// Called by MainController on each process frame.
    /// </summary>
    public void ProcessFrame()
    {
        if (_captureCountdown <= 0 || _subViewport == null) return;
        _captureCountdown--;
        if (_captureCountdown == 0)
        {
            CaptureAndSendFrame();
        }
    }

    private void CaptureAndSendFrame()
    {
        if (_subViewport == null) return;

        var viewportTexture = _subViewport.GetTexture();
        if (viewportTexture == null) return;

        var image = viewportTexture.GetImage();
        if (image == null) return;

        var pngBytes = image.SavePngToBuffer();
        var base64 = Convert.ToBase64String(pngBytes);
        var dataUrl = $"data:image/png;base64,{base64}";

        _dispatcher.Send(MessageTypes.Viewport, "preview", new
        {
            imageData = dataUrl,
            mode = "scene",
            contentType = "level",
            assetName = _pendingLevelName ?? _currentLevel?.LevelName ?? "Level",
            sceneInfo = new
            {
                actorCount = _actorMeshes.Count,
                levelName = _currentLevel?.LevelName ?? "",
            },
        });
    }

    // -----------------------------------------------------------------
    // Scene Lifecycle
    // -----------------------------------------------------------------

    private void ClearSceneNodes()
    {
        foreach (var meshInst in _actorMeshes.Values)
        {
            meshInst.QueueFree();
        }
        _actorMeshes.Clear();
        _currentLevel = null;
        _pendingLevelName = null;
    }

    /// <summary>
    /// Exits scene mode, clears all meshes, and notifies the frontend.
    /// </summary>
    public void ClearScene()
    {
        _loadCts?.Cancel();
        ClearSceneNodes();
        IsActive = false;
        _dispatcher.Send(MessageTypes.Scene, "cleared", new { });
    }

    // -----------------------------------------------------------------
    // Path Resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Resolves a game path from the import chain (e.g. "/Game/Environment/SM_Rock")
    /// to a path that the CUE4Parse file provider can load.
    /// Tries multiple formats similar to PreviewManager.ResolveGamePath.
    /// </summary>
    private string? ResolveGamePathForMesh(string meshPath)
    {
        if (_fileProvider == null) return null;

        // Strip leading slash from game path
        var normalized = meshPath.TrimStart('/');

        // Strip .uasset/.umap extension if present
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];
        if (normalized.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^5];

        var withExt = normalized + ".uasset";

        var projectDirName = Path.GetFileName(
            _projectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Try multiple path formats
        string[] candidates =
        {
            projectDirName + "/" + normalized,  // e.g. "ProjectName/Game/Env/SM_Rock"
            normalized,                          // e.g. "Game/Env/SM_Rock"
            projectDirName + "/" + withExt,       // e.g. "ProjectName/Game/Env/SM_Rock.uasset"
            withExt,                              // e.g. "Game/Env/SM_Rock.uasset"
        };

        foreach (var candidate in candidates)
        {
            if (_fileProvider.Files.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        // Try a filename-only fallback: find by the last path segment
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        if (!string.IsNullOrEmpty(fileName))
        {
            var suffix = "/" + fileName;
            var suffixExt = "/" + fileName + ".uasset";
            foreach (var key in _fileProvider.Files.Keys)
            {
                if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    key.EndsWith(suffixExt, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Resolved mesh path by filename: {Original} → {Resolved}", meshPath, key);
                    return key;
                }
            }
        }

        _logger.Debug("Could not resolve mesh path in provider: {Path} (tried {Count} formats)",
            meshPath, candidates.Length);
        return null;
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
            DisposeProvider();
            return;
        }

        var gameVersion = projectHandler!.EffectiveGameVersion;

        if (_fileProvider != null && _projectPath == currentProject.Path && _currentVersion == gameVersion)
        {
            return;
        }

        DisposeProvider();
        _projectPath = currentProject.Path;
        _currentVersion = gameVersion;

        _logger.Info("SceneManager creating CUE4Parse provider: {Path}, version: {Version}",
            _projectPath, gameVersion);

        CompressionInitializerFactory.EnsureInitialized(_logger);

        _fileProvider = new DefaultFileProvider(
            _projectPath,
            SearchOption.AllDirectories,
            versions: new VersionContainer(gameVersion),
            pathComparer: StringComparer.OrdinalIgnoreCase
        );

        _fileProvider.Initialize();
        await Task.Run(() => _fileProvider.Mount());

        _logger.Info("SceneManager provider ready: {FileCount} files", _fileProvider.Files.Count);
    }

    private void DisposeProvider()
    {
        _fileProvider?.Dispose();
        _fileProvider = null;
        _projectPath = null;
    }

    private static EngineVersion? MapEGameToEngineVersion(string eGameName)
    {
        var versionName = eGameName.Replace("GAME_", "VER_");
        if (Enum.TryParse<EngineVersion>(versionName, out var version))
            return version;
        return null;
    }

    // -----------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _loadCts?.Cancel();
        _loadCts?.Dispose();

        var selectionHandler = _dispatcher.GetHandler<SelectionHandler>();
        if (selectionHandler != null)
        {
            selectionHandler.SelectionChanged -= OnSelectionChanged;
        }

        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler != null)
        {
            projectHandler.GameVersionChanged -= OnGameVersionChanged;
        }

        ClearSceneNodes();
        DisposeProvider();

        _subViewport?.QueueFree();
        _subViewport = null;
        _camera = null;
        _light = null;
        _fillLight = null;
        _worldEnvironment = null;
        _sceneRoot = null;
    }
}

