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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Versions;
using Godot;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Assets.Compression;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Data;
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
    private readonly SubLevelDiscoverer _subLevelDiscoverer;
    private readonly LandscapeExtractor _landscapeExtractor;
    private AssetCache? _assetCache;

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

    // Selection state
    private string? _selectedActorId;

    // ID SubViewport outline system (screen-space edge detection)
    private SubViewport? _idSubViewport;
    private Camera3D? _idCamera;
    private Node3D? _idSceneRoot;
    private MeshInstance3D? _idMeshInstance;
    private StandardMaterial3D? _idMaterial;
    private CanvasLayer? _outlineCanvasLayer;
    private ColorRect? _outlineOverlay;
    private ShaderMaterial? _outlineShaderMaterial;
    private static readonly Color OutlineColor = new(1.0f, 0.6f, 0.0f, 1.0f);
    private const float OutlineThickness = 2.0f;

    // Rendering state
    private bool _doubleSided = true;
    private string _renderMode = "shaded";
    private int _captureCountdown;
    private string? _pendingLevelName;

    // Scene state
    private LevelExtractionResult? _currentLevel;
    private LevelExtractionResult[]? _allLevels;
    private SubLevelInfo[]? _subLevels;
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
        _subLevelDiscoverer = new SubLevelDiscoverer(logger);
        _landscapeExtractor = new LandscapeExtractor(logger);
    }

    /// <summary>
    /// Creates the SubViewport and subscribes to selection events.
    /// Must be called after dispatcher has registered all handlers.
    /// </summary>
    public void Initialize(Node parentNode)
    {
        _parentNode = parentNode;
        CreateSubViewport();
        CreateOutlineSystem();

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
    /// Loads a .umap level and its discovered sub-levels, progressively rendering all mesh actors.
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

            var projectPath = projectHandler.CurrentProject.Path;
            var fullPath = Path.Combine(projectPath, relativePath);
            if (!File.Exists(fullPath))
            {
                _logger.Warning("Level file not found: {Path}", fullPath);
                _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = false });
                IsActive = false;
                return;
            }

            // Map EGame to UAssetAPI EngineVersion
            var engineVersion = MapEGameToEngineVersion(projectHandler.EffectiveGameVersion.ToString());

            // Phase 0: Discover related sub-levels
            _logger.Info("Discovering sub-levels for: {Path}", fullPath);
            var subLevels = await Task.Run(() =>
                _subLevelDiscoverer.DiscoverSubLevels(fullPath, projectPath, engineVersion), ct);

            _subLevels = subLevels;
            var isMultiLevel = subLevels.Length > 1;

            if (isMultiLevel)
            {
                _logger.Info("Found {Count} sub-levels for {Path}", subLevels.Length, relativePath);
            }

            ct.ThrowIfCancellationRequested();

            // Phase 1: Extract actors from all sub-levels
            var levelResults = new List<LevelExtractionResult>();

            for (int i = 0; i < subLevels.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sl = subLevels[i];
                var slFullPath = Path.Combine(projectPath, sl.RelativePath);

                if (isMultiLevel)
                {
                    _dispatcher.Send(MessageTypes.Scene, "extractingLevel", new
                    {
                        levelIndex = i,
                        levelCount = subLevels.Length,
                        levelName = sl.LevelName,
                    });
                }

                _logger.Info("Extracting actors from sub-level {Index}/{Count}: {Name}",
                    i + 1, subLevels.Length, sl.LevelName);

                var result = await _levelExtractor.ExtractLevelAsync(
                    slFullPath, engineVersion, sl.LevelName, sl.PositionOffset,
                    progress: null, ct: ct);

                if (result != null)
                {
                    levelResults.Add(result);
                }
            }

            if (levelResults.Count == 0)
            {
                _logger.Warning("Failed to extract any levels from: {Path}", relativePath);
                _dispatcher.Send(MessageTypes.Scene, "loading", new { loading = false });
                IsActive = false;
                return;
            }

            ct.ThrowIfCancellationRequested();
            _currentLevel = levelResults[0];
            _allLevels = levelResults.ToArray();

            var primaryName = subLevels[0].LevelName;

            // Build combined actor list for frontend
            var allSceneActors = new List<SceneActor>();
            foreach (var level in levelResults)
            {
                foreach (var a in level.Actors)
                {
                    var offsetPos = a.Transform.Origin + level.PositionOffset;
                    allSceneActors.Add(new SceneActor(
                        a.Id, a.Name, a.ClassName, a.MeshPath,
                        new[] { offsetPos.X, offsetPos.Y, offsetPos.Z },
                        a.MeshPath != null, false,
                        level.LevelName
                    ));
                }
            }

            var meshActorCount = allSceneActors.Count(a => a.HasMesh);

            // Build sub-level summaries for frontend
            var levelSummaries = subLevels
                .Select((sl, idx) =>
                {
                    var lr = idx < levelResults.Count ? levelResults[idx] : null;
                    return new
                    {
                        name = sl.LevelName,
                        actorCount = lr?.Actors.Length ?? 0,
                        meshCount = lr?.Actors.Count(a => a.MeshPath != null) ?? 0,
                        source = sl.Source.ToString(),
                    };
                })
                .ToArray();

            _dispatcher.Send(MessageTypes.Scene, "actorList", new
            {
                levelName = primaryName,
                actors = allSceneActors,
                totalCount = allSceneActors.Count,
                meshCount = meshActorCount,
                isMultiLevel,
                subLevels = levelSummaries,
            });

            // Phase 2: Progressive mesh loading across all sub-levels
            var allMeshActors = levelResults
                .SelectMany(lr => lr.Actors
                    .Where(a => a.MeshPath != null)
                    .Select(a => (Actor: a, Offset: lr.PositionOffset)))
                .Take(MaxMeshActors)
                .ToArray();

            if (allMeshActors.Length > 0)
            {
                var samplePaths = allMeshActors.Take(5).Select(a => a.Actor.MeshPath).ToArray();
                _logger.Info("Scene: {Count} mesh actors to load across {Levels} levels. Sample paths: {Paths}",
                    allMeshActors.Length, levelResults.Count, string.Join(", ", samplePaths));

                await EnsureProviderAsync();

                // Open asset cache for pre-extracted .res lookups
                _assetCache?.Dispose();
                _assetCache = new AssetCache(_logger);
                _assetCache.Open(_projectPath!);

                if (_fileProvider == null && !(_assetCache?.HasCache == true))
                {
                    _logger.Warning("No CUE4Parse provider and no cache — cannot load meshes");
                    _dispatcher.Send(MessageTypes.Scene, "loaded", new
                    {
                        levelName = primaryName,
                        actorCount = 0,
                        subLevelCount = subLevels.Length,
                    });
                    return;
                }

                var sampleKeys = _fileProvider?.Files.Keys.Take(5).ToArray() ?? Array.Empty<string>();
                _logger.Info("Scene: Provider has {Count} files, cache={HasCache}. Sample keys: {Keys}",
                    _fileProvider?.Files.Count ?? 0, _assetCache?.HasCache ?? false,
                    string.Join(", ", sampleKeys));

                // Probe texture version before batch loading to detect wrong EGame
                ct.ThrowIfCancellationRequested();
                await ProbeTextureVersionAsync(ct);

                _pendingLevelName = primaryName;

                for (int i = 0; i < allMeshActors.Length; i += BatchSize)
                {
                    ct.ThrowIfCancellationRequested();

                    var batch = allMeshActors.Skip(i).Take(BatchSize);
                    foreach (var (actor, offset) in batch)
                    {
                        await LoadActorMeshAsync(actor, offset, ct);
                    }

                    var loaded = Math.Min(i + BatchSize, allMeshActors.Length);
                    _dispatcher.Send(MessageTypes.Scene, "loadProgress", new
                    {
                        loaded,
                        total = allMeshActors.Length,
                    });

                    // Capture a frame after each batch
                    RequestCapture();

                    // Yield to allow frame processing
                    await Task.Delay(1, ct);
                }
            }

            // Phase 2.5: Load landscape terrain meshes
            await LoadLandscapeActorsAsync(levelResults, ct);

            // Auto-frame the entire scene
            FrameScene();

            var totalMeshActorsAvailable = levelResults.Sum(lr => lr.Actors.Count(a => a.MeshPath != null));
            if (totalMeshActorsAvailable > MaxMeshActors)
            {
                _logger.Warning("Scene has {Total} mesh actors across {Levels} levels, limited to {Max}",
                    totalMeshActorsAvailable, levelResults.Count, MaxMeshActors);
            }

            _dispatcher.Send(MessageTypes.Scene, "loaded", new
            {
                levelName = primaryName,
                actorCount = _actorMeshes.Count,
                subLevelCount = subLevels.Length,
            });

            _logger.Info("Scene loaded: {Name} with {Count} meshes across {Levels} levels",
                primaryName, _actorMeshes.Count, subLevels.Length);
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

    private async Task LoadActorMeshAsync(ActorData actor, Vector3 positionOffset, CancellationToken ct)
    {
        if (actor.MeshPath == null) return;
        if (_fileProvider == null && !(_assetCache?.HasCache == true)) return;

        // Try loading from pre-extracted .res cache first
        if (_assetCache?.HasCache == true)
        {
            var cacheKey = ResolveToCachePath(actor.MeshPath);
            var cachedMeshPath = cacheKey != null ? _assetCache.GetMeshPath(cacheKey) : null;
            if (cachedMeshPath != null)
            {
                try
                {
                    var mesh = ResourceLoader.Load<ArrayMesh>(cachedMeshPath);
                    if (mesh != null)
                    {
                        var offsetTransformCached = actor.Transform;
                        offsetTransformCached.Origin += positionOffset;

                        var meshInstance = new MeshInstance3D
                        {
                            Mesh = mesh,
                            Transform = offsetTransformCached,
                            Name = actor.Name,
                        };

                        // Load materials from cache
                        var matKeys = _assetCache.GetMeshSurfaceMaterials(cacheKey!);
                        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
                        {
                            StandardMaterial3D? surfaceMat = null;
                            if (matKeys != null && i < matKeys.Length && matKeys[i] != null)
                            {
                                var matResPath = _assetCache.GetMaterialPath(matKeys[i]!);
                                if (matResPath != null)
                                {
                                    surfaceMat = ResourceLoader.Load<StandardMaterial3D>(matResPath);
                                }
                            }

                            if (surfaceMat == null)
                            {
                                surfaceMat = new StandardMaterial3D
                                {
                                    AlbedoColor = new Color(0.7f, 0.7f, 0.7f),
                                    Roughness = 0.6f,
                                    Metallic = 0.1f,
                                };
                            }

                            surfaceMat.CullMode = _doubleSided
                                ? BaseMaterial3D.CullModeEnum.Disabled
                                : BaseMaterial3D.CullModeEnum.Back;

                            mesh.SurfaceSetMaterial(i, surfaceMat);
                        }

                        _sceneRoot!.AddChild(meshInstance);
                        _actorMeshes[actor.Id] = meshInstance;
                        return; // Cache hit — skip CUE4Parse path entirely
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Cache load failed for {Name}, falling back to CUE4Parse: {Error}",
                        actor.Name, ex.Message);
                }
            }
        }

        if (_fileProvider == null) return;

        try
        {
            // Resolve the mesh game path to a provider-compatible load path
            var resolvedPath = ResolveGamePathForMesh(actor.MeshPath);
            if (resolvedPath == null)
            {
                _logger.Warning("Could not resolve mesh path: {Name} -> {Path}", actor.Name, actor.MeshPath);
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
                _logger.Warning("Could not load mesh for actor {Name}: {Path} (resolved: {Resolved})",
                    actor.Name, actor.MeshPath, resolvedPath);
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

            // Create MeshInstance3D at actor's transform with sub-level offset applied
            var offsetTransform = actor.Transform;
            offsetTransform.Origin += positionOffset;

            var meshInstance = new MeshInstance3D
            {
                Mesh = extractionResult.Mesh,
                Transform = offsetTransform,
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
    // Landscape Terrain Loading
    // -----------------------------------------------------------------

    private async Task LoadLandscapeActorsAsync(
        List<LevelExtractionResult> levelResults,
        CancellationToken ct)
    {
        if (_fileProvider == null) return;

        foreach (var level in levelResults)
        {
            var landscapeActors = level.Actors
                .Where(a => LevelExtractor.IsLandscapeActorClass(a.ClassName))
                .ToArray();

            if (landscapeActors.Length == 0) continue;

            _logger.Info("Loading {Count} landscape actors from {Level}",
                landscapeActors.Length, level.LevelName);

            // Resolve the .umap path in the file provider
            var levelGamePath = ResolveLevelGamePath(level.LevelName);
            if (levelGamePath == null)
            {
                _logger.Warning("Could not resolve level path for landscape: {Level}", level.LevelName);
                continue;
            }

            var meshResults = await _landscapeExtractor.ExtractLandscapeMeshesAsync(
                _fileProvider, levelGamePath, ct);

            if (meshResults.Count == 0) continue;

            // Use the first landscape actor's transform for positioning
            var actorTransform = landscapeActors[0].Transform;
            var offsetTransform = actorTransform;
            offsetTransform.Origin += level.PositionOffset;

            for (int i = 0; i < meshResults.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (mesh, name) = meshResults[i];

                var meshInstance = new MeshInstance3D
                {
                    Mesh = mesh,
                    Transform = offsetTransform,
                    Name = $"Landscape_{level.LevelName}_{i}",
                };

                // Apply default terrain material
                var material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.5f, 0.55f, 0.45f),
                    Roughness = 0.9f,
                    Metallic = 0.0f,
                    CullMode = _doubleSided
                        ? BaseMaterial3D.CullModeEnum.Disabled
                        : BaseMaterial3D.CullModeEnum.Back,
                };
                mesh.SurfaceSetMaterial(0, material);

                _sceneRoot!.AddChild(meshInstance);
                var actorId = $"{level.LevelName}:landscape-{i}";
                _actorMeshes[actorId] = meshInstance;
            }

            _logger.Info("Loaded {Count} terrain meshes for {Level}",
                meshResults.Count, level.LevelName);

            RequestCapture();
        }
    }

    /// <summary>
    /// Resolves a level name to a CUE4Parse-compatible game path for a .umap file.
    /// </summary>
    private string? ResolveLevelGamePath(string levelName)
    {
        if (_fileProvider == null) return null;

        var projectDirName = Path.GetFileName(
            _projectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Search provider files for .umap matching this level name
        var umapSuffix = "/" + levelName + ".umap";
        var umapSuffixAlt = "/" + levelName;

        foreach (var key in _fileProvider.Files.Keys)
        {
            if (key.EndsWith(umapSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // Return without .umap extension for LoadAllObjects
                return key[..^5];
            }
            if (key.EndsWith(umapSuffixAlt, StringComparison.OrdinalIgnoreCase) &&
                !key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        _logger.Debug("Could not find .umap in provider for level: {Name}", levelName);
        return null;
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

    // -----------------------------------------------------------------
    // Actor Selection & Outline
    // -----------------------------------------------------------------

    private void CreateOutlineSystem()
    {
        // --- ID SubViewport: renders selected mesh as white on transparent black ---
        _idSubViewport = new SubViewport
        {
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            TransparentBg = true,
            OwnWorld3D = true,
        };

        _idCamera = new Camera3D { Far = 1000000f };
        _idSceneRoot = new Node3D { Name = "IdSceneRoot" };

        _idMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Colors.White,
        };

        _idSubViewport.AddChild(_idCamera);
        _idSubViewport.AddChild(_idSceneRoot);
        // Child of the main SubViewport so Godot renders it first (deeper viewports
        // render before shallower ones), ensuring the outline shader always reads
        // a fresh ID texture rather than the previous frame's stale data.
        _subViewport!.AddChild(_idSubViewport);

        // --- Edge detection overlay inside main SubViewport ---
        _outlineCanvasLayer = new CanvasLayer { Layer = 1 };
        _subViewport!.AddChild(_outlineCanvasLayer);

        _outlineOverlay = new ColorRect
        {
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            Size = new Vector2(ViewportWidth, ViewportHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var shader = GD.Load<Shader>("res://shaders/selection_outline.gdshader");
        _outlineShaderMaterial = new ShaderMaterial { Shader = shader };
        _outlineShaderMaterial.SetShaderParameter("outline_color", OutlineColor);
        _outlineShaderMaterial.SetShaderParameter("thickness", OutlineThickness);
        // Bind the ID SubViewport's texture as the input for edge detection
        _outlineShaderMaterial.SetShaderParameter("id_texture", _idSubViewport.GetTexture());

        _outlineOverlay.Material = _outlineShaderMaterial;
        _outlineCanvasLayer.AddChild(_outlineOverlay);
    }

    /// <summary>
    /// Sets the selected actor, applying/removing the outline and notifying the frontend.
    /// Pass null to deselect.
    /// </summary>
    public void SetActorSelected(string? actorId)
    {
        if (_selectedActorId == actorId) return;

        // Remove outline from previous selection
        RemoveOutline();

        _selectedActorId = actorId;

        // Apply outline to new selection
        if (actorId != null && _actorMeshes.TryGetValue(actorId, out var newMesh))
        {
            ApplyOutline(newMesh);
        }

        RequestCapture();
        _dispatcher.Send(MessageTypes.Scene, "actorSelected", new { actorId });
    }

    /// <summary>
    /// Focus the camera on a specific actor's bounding box.
    /// </summary>
    public void FocusActor(string actorId)
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
    }

    /// <summary>
    /// Selects an actor and focuses the camera on it (used by outliner double-click / focusActor).
    /// </summary>
    public void SelectActor(string actorId)
    {
        SetActorSelected(actorId);
        FocusActor(actorId);
    }

    private void ApplyOutline(MeshInstance3D meshInst)
    {
        if (meshInst.Mesh == null || _idSceneRoot == null || _idMaterial == null) return;

        // Remove any existing ID mesh
        RemoveOutline();

        // Create a duplicate MeshInstance3D with the same mesh and transform,
        // but override all surfaces with unshaded white material
        _idMeshInstance = new MeshInstance3D
        {
            Mesh = meshInst.Mesh,
            Transform = meshInst.Transform,
        };

        for (int i = 0; i < meshInst.Mesh.GetSurfaceCount(); i++)
        {
            _idMeshInstance.SetSurfaceOverrideMaterial(i, _idMaterial);
        }

        _idSceneRoot.AddChild(_idMeshInstance);
    }

    private void RemoveOutline()
    {
        if (_idMeshInstance != null)
        {
            // Remove from tree immediately so it won't render this frame.
            // QueueFree alone defers removal to end-of-frame, which would
            // cause a ghost outline for one frame when switching selections.
            _idSceneRoot?.RemoveChild(_idMeshInstance);
            _idMeshInstance.QueueFree();
            _idMeshInstance = null;
        }
    }

    // -----------------------------------------------------------------
    // Ray Picking
    // -----------------------------------------------------------------

    /// <summary>
    /// Performs AABB-based ray picking from normalized screen coordinates (0-1).
    /// Returns the actor ID of the closest hit, or null if nothing was hit.
    /// </summary>
    public string? PickActorAtScreenPosition(float normalizedX, float normalizedY)
    {
        if (_camera == null || _subViewport == null || _actorMeshes.Count == 0)
            return null;

        var screenPos = new Vector2(normalizedX * ViewportWidth, normalizedY * ViewportHeight);
        var rayOrigin = _camera.ProjectRayOrigin(screenPos);
        var rayNormal = _camera.ProjectRayNormal(screenPos);

        string? closestActorId = null;
        float closestDistance = float.MaxValue;

        foreach (var (actorId, meshInst) in _actorMeshes)
        {
            if (meshInst.Mesh == null) continue;

            var localAabb = meshInst.GetAabb();
            var worldAabb = meshInst.Transform * localAabb;

            if (IntersectRayAabb(rayOrigin, rayNormal, worldAabb, out float distance))
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestActorId = actorId;
                }
            }
        }

        return closestActorId;
    }

    private static bool IntersectRayAabb(Vector3 rayOrigin, Vector3 rayDirection, Aabb aabb, out float distance)
    {
        distance = 0f;
        var invDir = new Vector3(
            rayDirection.X != 0 ? 1f / rayDirection.X : float.MaxValue,
            rayDirection.Y != 0 ? 1f / rayDirection.Y : float.MaxValue,
            rayDirection.Z != 0 ? 1f / rayDirection.Z : float.MaxValue
        );

        var t1 = (aabb.Position.X - rayOrigin.X) * invDir.X;
        var t2 = (aabb.End.X - rayOrigin.X) * invDir.X;
        var t3 = (aabb.Position.Y - rayOrigin.Y) * invDir.Y;
        var t4 = (aabb.End.Y - rayOrigin.Y) * invDir.Y;
        var t5 = (aabb.Position.Z - rayOrigin.Z) * invDir.Z;
        var t6 = (aabb.End.Z - rayOrigin.Z) * invDir.Z;

        var tMin = Math.Max(Math.Max(Math.Min(t1, t2), Math.Min(t3, t4)), Math.Min(t5, t6));
        var tMax = Math.Min(Math.Min(Math.Max(t1, t2), Math.Max(t3, t4)), Math.Max(t5, t6));

        if (tMax < 0 || tMin > tMax)
            return false;

        distance = tMin >= 0 ? tMin : tMax;
        return true;
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

        // Sync the ID camera so the silhouette matches the main viewport
        if (_idCamera != null)
        {
            _idCamera.Position = _camera.Position;
            _idCamera.LookAt(_cameraTarget);
        }
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

        // Trigger ID SubViewport render first so the edge detection shader has fresh data
        if (_idSubViewport != null)
        {
            _idSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }

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
        _selectedActorId = null;
        RemoveOutline();
        foreach (var meshInst in _actorMeshes.Values)
        {
            meshInst.QueueFree();
        }
        _actorMeshes.Clear();
        _currentLevel = null;
        _allLevels = null;
        _subLevels = null;
        _pendingLevelName = null;
        _assetCache?.Dispose();
        _assetCache = null;
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
    // EGame Version Fallback
    // -----------------------------------------------------------------

    /// <summary>
    /// EGame versions to try when the current version produces empty PlatformData.
    /// Same list as PreviewManager. The key boundary is UE4.20 where the skip
    /// offset in DeserializeCookedPlatformData changes from int32 to int64.
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
    /// Probes a texture from the provider to detect wrong EGame version.
    /// If PlatformData is empty, triggers version fallback before batch loading.
    /// </summary>
    private async Task ProbeTextureVersionAsync(CancellationToken ct)
    {
        if (_fileProvider == null) return;

        // Find any texture-looking key in the provider to probe
        var texturePath = _fileProvider.Files.Keys
            .FirstOrDefault(k =>
                k.Contains("Texture", StringComparison.OrdinalIgnoreCase) ||
                k.EndsWith("_D.uasset", StringComparison.OrdinalIgnoreCase));

        if (texturePath == null)
        {
            _logger.Debug("Scene: No texture found in provider for version probe");
            return;
        }

        var loadPath = texturePath;
        if (loadPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            loadPath = loadPath[..^7];

        var texture = await Task.Run(() =>
        {
            try { return _fileProvider.LoadPackageObject<UTexture2D>(loadPath); }
            catch { return null; }
        }, ct);

        if (texture != null && texture.PlatformData.SizeX == 0 && texture.PlatformData.SizeY == 0)
        {
            _logger.Warning("Scene: Texture probe has empty PlatformData ({Path}), trying version fallback...",
                loadPath);
            await TryVersionFallbackAsync(loadPath);
        }
        else if (texture != null)
        {
            _logger.Info("Scene: Texture probe OK — {W}x{H} ({Path})",
                texture.PlatformData.SizeX, texture.PlatformData.SizeY, loadPath);
        }
    }

    /// <summary>
    /// Tries loading a texture with different EGame versions when the current
    /// version produces empty PlatformData. Updates the provider version on success.
    /// </summary>
    private async Task<bool> TryVersionFallbackAsync(string loadPath)
    {
        if (_fileProvider == null) return false;

        var originalVersion = _fileProvider.Versions.Game;

        foreach (var version in VersionFallbacks)
        {
            if (version == originalVersion) continue;

            _logger.Info("Scene: Trying EGame fallback: {Version}", version);
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
                    _logger.Info("Scene: Version fallback succeeded with {Version}: {W}x{H}",
                        version, texture.PlatformData.SizeX, texture.PlatformData.SizeY);

                    _currentVersion = version;
                    var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
                    if (projectHandler != null && _projectPath != null)
                    {
                        projectHandler.SetGameVersionFromImport(_projectPath, version.ToString());
                        _logger.Info("Scene: Auto-corrected game version to {Version}", version);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Scene: Fallback {Version} failed: {Error}", version, ex.Message);
            }
        }

        // None worked — restore original version
        _fileProvider.Versions.Game = originalVersion;
        _logger.Warning("Scene: No EGame version produced valid texture data");
        return false;
    }

    // -----------------------------------------------------------------
    // Path Resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Converts a UE game path (e.g., "/Game/Env/SM_Rock") to a DB-relative asset path
    /// (e.g., "Content/Env/SM_Rock.uasset") for cache lookups.
    /// </summary>
    private string? ResolveToCachePath(string gamePath)
    {
        if (_fileProvider == null && !(_assetCache?.HasCache == true)) return null;

        // If we have a provider, use existing resolution and strip prefix
        if (_fileProvider != null)
        {
            var resolved = ResolveGamePathForMesh(gamePath);
            if (resolved != null)
            {
                // Strip project directory prefix that CUE4Parse adds
                var projectDirName = Path.GetFileName(
                    _projectPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                var result = resolved;
                if (result.StartsWith(projectDirName + "/", StringComparison.OrdinalIgnoreCase))
                    result = result[(projectDirName.Length + 1)..];

                // Ensure .uasset extension
                if (!result.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                    !result.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                    result += ".uasset";

                return result;
            }
        }

        // Fallback: basic game path normalization
        var normalized = gamePath.TrimStart('/');
        if (!normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized += ".uasset";

        // Convert /Game/ prefix to Content/ (standard UE convention)
        if (normalized.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
            normalized = "Content/" + normalized[5..];

        return normalized;
    }

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
        _assetCache?.Dispose();
        _assetCache = null;

        // Dispose outline system resources
        _outlineShaderMaterial?.Dispose();
        _idMaterial?.Dispose();
        _outlineOverlay?.QueueFree();
        _outlineCanvasLayer?.QueueFree();
        _idSubViewport?.QueueFree();
        _idSubViewport = null;
        _idCamera = null;
        _idSceneRoot = null;
        _outlineOverlay = null;
        _outlineCanvasLayer = null;
        _outlineShaderMaterial = null;
        _idMaterial = null;

        _subViewport?.QueueFree();
        _subViewport = null;
        _camera = null;
        _light = null;
        _fillLight = null;
        _worldEnvironment = null;
        _sceneRoot = null;
    }
}

