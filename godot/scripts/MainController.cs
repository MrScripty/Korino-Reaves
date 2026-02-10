// Main Controller - Entry point for UAsset Viewer
//
// Manages application lifecycle:
// - CEF initialization (via Rust GDExtension node)
// - Browser creation
// - IPC dispatcher setup
// - Input forwarding

using System;
using System.Threading.Tasks;
using Godot;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Assets;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Data;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using UAssetViewer.Rendering;

namespace UAssetViewer;

/// <summary>
/// Main controller for the UAsset Viewer application.
/// Manages CEF lifecycle via the Rust CefBrowserNode GDExtension and
/// coordinates between browser and Godot.
/// </summary>
public partial class MainController : Node
{
    /// <summary>
    /// Path to the Svelte UI HTML file. Set in editor or override.
    /// </summary>
    [Export]
    public string UiPath { get; set; } = "file://{UI_PATH}/index.html";

    private IAppLogger _logger = null!;
    private AssetManager? _assetManager;
    private EditDatabase? _editDatabase;
    private Node? _cefNode;
    private IpcDispatcher? _dispatcher;
    private PreviewManager? _previewManager;
    private SceneManager? _sceneManager;
    private TextureRect? _overlay;
    private bool _browserCreated;

    public override void _Ready()
    {
        _logger = AppLogger.Instance;
        using var scope = _logger.BeginScope("MainController._Ready");

        try
        {
            // Get UI overlay
            _overlay = GetNode<TextureRect>("UIOverlay");

            // Initialize CEF via GDExtension node
            InitializeCef();

            // Create browser
            CreateBrowser();

            // Set up IPC dispatcher
            SetupDispatcher();

            _logger.Info("UAsset Viewer initialized");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize UAsset Viewer");
            GD.PrintErr($"Initialization failed: {ex.Message}");
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_cefNode == null || !_browserCreated)
        {
            return;
        }

        // Forward input to CEF based on event type
        switch (@event)
        {
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;

            case InputEventMouseButton button:
                HandleMouseButton(button);
                break;

            case InputEventKey key:
                HandleKey(key);
                break;
        }
    }

    public override void _Process(double delta)
    {
        _previewManager?.ProcessFrame();
        _sceneManager?.ProcessFrame();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Cleanup();
        }
    }

    private void InitializeCef()
    {
        _logger.Info("Initializing CEF...");

        // Instantiate the Rust GDExtension CefBrowserNode
        _cefNode = ClassDB.Instantiate("CefBrowserNode").As<Node>();
        _cefNode.Name = "CefBrowser";
        AddChild(_cefNode);

        // Find the CEF helper binary (Rust cef-helper-rs)
        string helperPath = "";
        var possiblePaths = new[]
        {
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cef-helper"),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "cef-helper-rs", "target", "release", "cef-helper"),
            ProjectSettings.GlobalizePath("res://bin/cef-helper"),
        };

        foreach (var path in possiblePaths)
        {
            if (System.IO.File.Exists(path))
            {
                helperPath = path;
                break;
            }
        }

        // CEF_PATH env var for locating native CEF libs
        var cefPath = System.Environment.GetEnvironmentVariable("CEF_PATH") ?? "";

        var success = (bool)_cefNode.Call("initialize", helperPath, cefPath);
        if (!success)
        {
            _logger.Error("Failed to initialize CEF via GDExtension");
            GD.PrintErr("CEF initialization failed");
        }
    }

    private void CreateBrowser()
    {
        if (_cefNode == null)
        {
            return;
        }

        _logger.Info("Creating browser...");

        var viewportSize = GetViewport().GetVisibleRect().Size;
        var width = (int)viewportSize.X;
        var height = (int)viewportSize.Y;

        // Check if Svelte dev server is running (preferred for development)
        var devServerUrl = "http://localhost:5173";
        bool useDevServer = false;

        _logger.Info("Checking for Svelte dev server at {Url}...", devServerUrl);
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(1000);
            var response = client.GetAsync(devServerUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                useDevServer = true;
                _logger.Info("Found Svelte dev server at {Url}", devServerUrl);
            }
            else
            {
                _logger.Warning("Dev server responded with status {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.Info("Dev server not available ({Message}), using file:// fallback", ex.InnerException?.Message ?? ex.Message);
        }

        string resolvedPath;
        if (useDevServer)
        {
            resolvedPath = devServerUrl;
        }
        else
        {
            // Fallback to file:// URL with built assets
            resolvedPath = UiPath;
            if (resolvedPath.Contains("{UI_PATH}"))
            {
                var uiBasePath = ProjectSettings.GlobalizePath("res://ui");
                if (!System.IO.Directory.Exists(uiBasePath))
                {
                    // Fallback to svelte-ui build output
                    uiBasePath = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "..",
                            "svelte-ui",
                            "dist"
                        )
                    );
                }
                resolvedPath = resolvedPath.Replace("{UI_PATH}", uiBasePath);
            }
            _logger.Info("Loading UI from: {Path}", resolvedPath);
        }

        _browserCreated = (bool)_cefNode.Call("create_browser", resolvedPath, width, height);

        if (_browserCreated)
        {
            // Apply BGRA→RGBA swizzle shader so we can pass CEF's BGRA
            // bytes directly without CPU-side conversion.
            var shader = GD.Load<Shader>("res://shaders/bgra_swizzle.gdshader");
            _overlay!.Material = new ShaderMaterial { Shader = shader };

            // Connect to framebuffer_updated signal — the texture is created
            // on the first OnPaint callback in the Rust node, so we fetch it
            // from the signal handler rather than immediately.
            _cefNode.Connect("framebuffer_updated", Callable.From(OnFramebufferUpdated));
        }
        else
        {
            _logger.Error("Failed to create CEF browser");
        }
    }

    private void OnFramebufferUpdated()
    {
        if (_cefNode == null || _overlay == null)
        {
            return;
        }

        // Fetch texture from Rust node and assign to overlay.
        // On first call this sets the texture; on subsequent calls it updates
        // the reference if the texture was recreated (e.g. after resize).
        var tex = _cefNode.Call("get_texture").As<ImageTexture>();

        GD.Print($"[MainController] OnFramebufferUpdated called: tex={tex?.GetInstanceId()}, current={_overlay.Texture?.GetInstanceId()}");

        if (tex != null)
        {
            if (_overlay.Texture == null || _overlay.Texture.GetInstanceId() != tex.GetInstanceId())
            {
                GD.Print($"[MainController] Assigning texture to overlay: {tex.GetInstanceId()} ({tex.GetWidth()}x{tex.GetHeight()})");
                _overlay.Texture = tex;
            }
        }
    }

    private void SetupDispatcher()
    {
        if (_cefNode == null)
        {
            return;
        }

        _logger.Info("Setting up IPC dispatcher...");

        // Create AssetManager for handling asset operations
        _assetManager = new AssetManager(_logger);
        _editDatabase = new EditDatabase(_logger);

        _dispatcher = new IpcDispatcher(_logger, _assetManager);
        _dispatcher.RegisterDefaultHandlers();
        _dispatcher.RegisterDialogHandler(this);

        // Register PropertyHandler with edit database and dispatcher
        var propertyHandler = new PropertyHandler(_logger, _assetManager, _editDatabase, _dispatcher);
        _dispatcher.RegisterHandler(propertyHandler);

        // Create preview manager and scene manager (depends on dispatcher having handlers)
        _previewManager = new PreviewManager(_logger, _dispatcher);
        _previewManager.Initialize(this);

        _sceneManager = new SceneManager(_logger, _dispatcher);
        _sceneManager.Initialize(this);

        // Register viewport handler with both managers for camera routing
        _dispatcher.RegisterHandler(new ViewportHandler(_logger, _previewManager, _sceneManager));
        _dispatcher.RegisterHandler(new SceneHandler(_logger, _sceneManager));

        // Open/close edit database when project opens/closes
        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler != null)
        {
            projectHandler.ProjectOpened += (project) =>
            {
                _editDatabase!.Open(project.Path);
                propertyHandler.PushEditedFiles();
            };

            projectHandler.ProjectClosed += () =>
            {
                _editDatabase!.Close();
            };
        }

        // Auto-load assets and push properties when selection changes
        var selectionHandler = _dispatcher.GetHandler<SelectionHandler>();
        if (selectionHandler != null)
        {
            selectionHandler.SelectionChanged += (state) =>
            {
                if (state.SelectedId == null) return;

                if (IsAssetFileNode(state.SelectedId))
                {
                    // Auto-load .uasset files when selected in the project tree
                    _ = AutoLoadAssetAsync(state.SelectedId, propertyHandler);
                }
                else
                {
                    propertyHandler.PushPropertiesForNode(state.SelectedId, _dispatcher!);
                }
            };
            _logger.Info("Selection auto-load and property push subscribed to SelectionChanged");
        }

        _dispatcher.Connect(_cefNode);
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        var pos = motion.Position;
        _cefNode!.Call("send_mouse_move", (int)pos.X, (int)pos.Y);
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        var pos = button.Position;

        // Map Godot mouse button to CEF: 0=Left, 1=Middle, 2=Right
        int cefButton = button.ButtonIndex switch
        {
            MouseButton.Left => 0,
            MouseButton.Middle => 1,
            MouseButton.Right => 2,
            _ => -1,
        };

        if (cefButton >= 0)
        {
            _cefNode!.Call(
                "send_mouse_button",
                (int)pos.X,
                (int)pos.Y,
                cefButton,
                button.Pressed,
                button.DoubleClick ? 2 : 1
            );
        }

        // Handle scroll wheel
        if (button.ButtonIndex == MouseButton.WheelUp)
        {
            _cefNode!.Call("send_mouse_wheel", (int)pos.X, (int)pos.Y, 0, 120);
        }
        else if (button.ButtonIndex == MouseButton.WheelDown)
        {
            _cefNode!.Call("send_mouse_wheel", (int)pos.X, (int)pos.Y, 0, -120);
        }
    }

    private void HandleKey(InputEventKey key)
    {
        // Check for Ctrl+Shift+I to open DevTools
        if (key.Pressed && key.Keycode == Key.I && key.CtrlPressed && key.ShiftPressed)
        {
            _logger.Info("Opening CEF DevTools...");
            _cefNode!.Call("show_dev_tools");
            GetViewport().SetInputAsHandled();
            return;
        }

        // event_type: 0=KeyDown, 1=KeyUp, 2=Char
        int eventType = key.Pressed ? 0 : 1;
        int modifiers = GetModifierFlags(key);

        _cefNode!.Call(
            "send_key_event",
            eventType,
            (int)key.Keycode,
            (int)key.PhysicalKeycode,
            modifiers,
            (int)key.Unicode
        );

        // Send char event for printable characters
        if (key.Pressed && key.Unicode != 0)
        {
            _cefNode.Call(
                "send_key_event",
                2, // Char
                (int)key.Unicode,
                (int)key.PhysicalKeycode,
                modifiers,
                (int)key.Unicode
            );
        }
    }

    private static int GetModifierFlags(InputEventWithModifiers evt)
    {
        int flags = 0;

        // CEF modifier flag values
        if (evt.ShiftPressed) flags |= 1 << 1;   // EVENTFLAG_SHIFT_DOWN
        if (evt.CtrlPressed) flags |= 1 << 2;     // EVENTFLAG_CONTROL_DOWN
        if (evt.AltPressed) flags |= 1 << 3;      // EVENTFLAG_ALT_DOWN
        if (evt.MetaPressed) flags |= 1 << 7;     // EVENTFLAG_COMMAND_DOWN

        return flags;
    }

    private static bool IsAssetFileNode(string nodeId)
    {
        return nodeId.StartsWith("file:") &&
               (nodeId.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
                nodeId.EndsWith(".umap", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the export with the most properties to display.
    /// For textures/meshes export-0 is the main asset, but for Blueprints
    /// export-0 is the class definition (few/no properties) and export-1 is
    /// the CDO with actual property values.
    /// </summary>
    private string FindBestExportForProperties()
    {
        var asset = _assetManager?.CurrentUAsset;
        if (asset == null) return "export-0";

        string best = "export-0";
        int bestCount = 0;

        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i] is UAssetAPI.ExportTypes.NormalExport normal && normal.Data.Count > bestCount)
            {
                bestCount = normal.Data.Count;
                best = $"export-{i}";
            }
        }

        return best;
    }

    /// <summary>
    /// Maps a CUE4Parse EGame name to UAssetAPI EngineVersion.
    /// EGame uses "GAME_UE4_22" format, EngineVersion uses "VER_UE4_22" format.
    /// </summary>
    private static EngineVersion? MapEGameToEngineVersion(string eGameName)
    {
        var versionName = eGameName.Replace("GAME_", "VER_");
        if (Enum.TryParse<EngineVersion>(versionName, out var version))
            return version;
        return null;
    }

    private async Task AutoLoadAssetAsync(string fileNodeId, PropertyHandler propertyHandler)
    {
        try
        {
            var projectHandler = _dispatcher!.GetHandler<ProjectHandler>();
            if (projectHandler?.CurrentProject == null)
            {
                _logger.Warning("Cannot auto-load asset: no project open");
                return;
            }

            var relativePath = fileNodeId.Substring(5); // Remove "file:" prefix
            var fullPath = System.IO.Path.Combine(projectHandler.CurrentProject.Path, relativePath);

            if (!System.IO.File.Exists(fullPath))
            {
                _logger.Warning("Asset file not found: {Path}", fullPath);
                return;
            }

            _logger.Info("Auto-loading asset: {Path}", fullPath);

            // Map the project's EGame version to UAssetAPI EngineVersion
            var engineVersion = MapEGameToEngineVersion(projectHandler.EffectiveGameVersion.ToString());
            var assetInfo = await _assetManager!.LoadAsync(fullPath, engineVersion);

            // Set file path for edit tracking and reapply saved edits
            propertyHandler.SetCurrentFilePath(relativePath);
            propertyHandler.ReapplyEdits();

            // Notify frontend of loaded asset
            _dispatcher.Send(MessageTypes.Asset, "opened", assetInfo);

            // Push properties for the best export to the Properties panel.
            // For textures/meshes export-0 has the data, but for Blueprints
            // the first export is the class definition (empty) and the CDO
            // (export-1) holds the actual property values.
            var bestExport = FindBestExportForProperties();
            propertyHandler.PushPropertiesForNode(bestExport, _dispatcher);

            _logger.Info("Asset auto-loaded: {File}", assetInfo.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-load asset: {Id}", fileNodeId);
            _dispatcher!.Send(MessageTypes.Asset, "error",
                new { message = $"Failed to load asset: {ex.Message}" });
        }
    }

    private void Cleanup()
    {
        _logger.Info("Cleaning up...");

        _editDatabase?.Dispose();
        _sceneManager?.Dispose();
        _previewManager?.Dispose();
        _dispatcher?.Dispose();

        if (_cefNode != null)
        {
            _cefNode.Call("shutdown");
            _cefNode.QueueFree();
            _cefNode = null;
        }

        _logger.Info("Cleanup complete");
    }
}
