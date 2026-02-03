// Main Controller - Entry point for UAsset Viewer
//
// Manages application lifecycle:
// - CEF initialization
// - Browser creation
// - IPC dispatcher setup
// - Texture update loop

using System;
using Godot;
using UAssetViewer.Assets;
using UAssetViewer.Bridge;
using UAssetViewer.Cef;
using UAssetViewer.Infrastructure;

namespace UAssetViewer;

/// <summary>
/// Main controller for the UAsset Viewer application.
/// Manages CEF lifecycle and coordinates between browser and Godot.
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
    private CefBrowserWrapper? _browser;
    private IpcDispatcher? _dispatcher;
    private TextureRect? _overlay;
    private ImageTexture? _texture;
    private Image? _image;

    public override void _Ready()
    {
        _logger = AppLogger.Instance;
        using var scope = _logger.BeginScope("MainController._Ready");

        try
        {
            // Get UI overlay
            _overlay = GetNode<TextureRect>("UIOverlay");

            // Initialize CEF
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

    public override void _Process(double delta)
    {
        // Pump CEF message loop
        if (CefManager.Instance.IsInitialized)
        {
            CefManager.Instance.DoMessageLoopWork();
        }

        // Update texture if framebuffer changed
        UpdateTexture();
    }

    public override void _Input(InputEvent @event)
    {
        if (_browser == null || !_browser.IsCreated)
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

        // Look for CEF helper in typical locations
        string? helperPath = null;
        var possiblePaths = new[]
        {
            "res://CefHelper",
            "res://CefHelper.exe",
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CefHelper"),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CefHelper.exe"),
        };

        foreach (var path in possiblePaths)
        {
            var resolved = path.StartsWith("res://")
                ? ProjectSettings.GlobalizePath(path)
                : path;

            if (System.IO.File.Exists(resolved))
            {
                helperPath = resolved;
                break;
            }
        }

        CefManager.Instance.Initialize(helperPath);
    }

    private void CreateBrowser()
    {
        _logger.Info("Creating browser...");

        var viewportSize = GetViewport().GetVisibleRect().Size;
        var width = (int)viewportSize.X;
        var height = (int)viewportSize.Y;

        _browser = new CefBrowserWrapper(_logger);

        // Resolve UI path
        var resolvedPath = UiPath;
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
                        "build"
                    )
                );
            }
            resolvedPath = resolvedPath.Replace("{UI_PATH}", uiBasePath);
        }

        _logger.Info("Loading UI from: {Path}", resolvedPath);
        _browser.Create(resolvedPath, width, height);

        // Create image and texture for display
        _image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        _overlay!.Texture = _texture;
    }

    private void SetupDispatcher()
    {
        _logger.Info("Setting up IPC dispatcher...");

        // Create AssetManager for handling asset operations
        _assetManager = new AssetManager(_logger);

        _dispatcher = new IpcDispatcher(_logger, _assetManager);
        _dispatcher.RegisterDefaultHandlers();
        _dispatcher.Connect(_browser!);
    }

    private void UpdateTexture()
    {
        if (_browser == null || _texture == null || _image == null)
        {
            return;
        }

        var capture = _browser.CaptureIfDirty();
        if (capture == null)
        {
            return;
        }

        var (data, width, height) = capture.Value;

        // Resize image if needed
        if (_image.GetWidth() != width || _image.GetHeight() != height)
        {
            _image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        }

        // Convert BGRA to RGBA and copy to image
        var rgbaData = ConvertBgraToRgba(data);
        _image.SetData(width, height, false, Image.Format.Rgba8, rgbaData);
        _texture.Update(_image);
    }

    private static byte[] ConvertBgraToRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];     // R
            rgba[i + 1] = bgra[i + 1]; // G
            rgba[i + 2] = bgra[i];     // B
            rgba[i + 3] = bgra[i + 3]; // A
        }
        return rgba;
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        var pos = motion.Position;
        _browser!.SendMouseMove((int)pos.X, (int)pos.Y);
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        var pos = button.Position;
        var cefButton = button.ButtonIndex switch
        {
            MouseButton.Left => Xilium.CefGlue.CefMouseButtonType.Left,
            MouseButton.Right => Xilium.CefGlue.CefMouseButtonType.Right,
            MouseButton.Middle => Xilium.CefGlue.CefMouseButtonType.Middle,
            _ => (Xilium.CefGlue.CefMouseButtonType?)null,
        };

        if (cefButton.HasValue)
        {
            _browser!.SendMouseButton(
                (int)pos.X,
                (int)pos.Y,
                cefButton.Value,
                button.Pressed,
                button.DoubleClick ? 2 : 1
            );
        }

        // Handle scroll wheel
        if (button.ButtonIndex == MouseButton.WheelUp)
        {
            _browser!.SendMouseWheel((int)pos.X, (int)pos.Y, 0, 120);
        }
        else if (button.ButtonIndex == MouseButton.WheelDown)
        {
            _browser!.SendMouseWheel((int)pos.X, (int)pos.Y, 0, -120);
        }
    }

    private void HandleKey(InputEventKey key)
    {
        var cefKey = new Xilium.CefGlue.CefKeyEvent
        {
            EventType = key.Pressed
                ? Xilium.CefGlue.CefKeyEventType.KeyDown
                : Xilium.CefGlue.CefKeyEventType.KeyUp,
            WindowsKeyCode = (int)key.Keycode,
            NativeKeyCode = (int)key.PhysicalKeycode,
            Modifiers = GetCefModifiers(key),
        };

        _browser!.SendKeyEvent(cefKey);

        // Send char event for printable characters
        if (key.Pressed && key.Unicode != 0)
        {
            var charEvent = new Xilium.CefGlue.CefKeyEvent
            {
                EventType = Xilium.CefGlue.CefKeyEventType.Char,
                Character = (char)key.Unicode,
                UnmodifiedCharacter = (char)key.Unicode,
                WindowsKeyCode = key.Unicode,
                Modifiers = GetCefModifiers(key),
            };
            _browser.SendKeyEvent(charEvent);
        }
    }

    private static Xilium.CefGlue.CefEventFlags GetCefModifiers(InputEventWithModifiers evt)
    {
        var flags = Xilium.CefGlue.CefEventFlags.None;

        if (evt.ShiftPressed) flags |= Xilium.CefGlue.CefEventFlags.ShiftDown;
        if (evt.CtrlPressed) flags |= Xilium.CefGlue.CefEventFlags.ControlDown;
        if (evt.AltPressed) flags |= Xilium.CefGlue.CefEventFlags.AltDown;
        if (evt.MetaPressed) flags |= Xilium.CefGlue.CefEventFlags.CommandDown;

        return flags;
    }

    private void Cleanup()
    {
        _logger.Info("Cleaning up...");

        _dispatcher?.Dispose();
        _browser?.Dispose();
        CefManager.Instance.Dispose();

        _logger.Info("Cleanup complete");
    }
}
