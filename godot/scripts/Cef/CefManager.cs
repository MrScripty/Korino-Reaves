// CEF Manager - Singleton managing CEF lifecycle
//
// Handles:
// - CEF initialization with offscreen rendering
// - Helper binary path configuration
// - Message pump integration with Godot _Process()
// - Clean shutdown

using System;
using System.IO;
using UAssetViewer.Infrastructure;
using Xilium.CefGlue;

namespace UAssetViewer.Cef;

/// <summary>
/// Singleton managing CEF lifecycle and message pump.
/// Must be initialized before creating any browsers.
/// </summary>
public sealed class CefManager : IDisposable
{
    private static CefManager? _instance;
    private static readonly object InstanceLock = new();

    private readonly IAppLogger _logger;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets the singleton instance of CefManager.
    /// </summary>
    public static CefManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (InstanceLock)
                {
                    _instance ??= new CefManager(AppLogger.Instance);
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Gets whether CEF has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    private CefManager(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes CEF with offscreen rendering enabled.
    /// Must be called once before creating browsers.
    /// </summary>
    /// <param name="cefHelperPath">Path to the CEF helper executable</param>
    /// <exception cref="InvalidOperationException">If already initialized</exception>
    /// <exception cref="CefException">If CEF initialization fails</exception>
    public void Initialize(string? cefHelperPath = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            throw new InvalidOperationException("CEF is already initialized");
        }

        using var scope = _logger.BeginScope("CefManager.Initialize");
        _logger.Info("Initializing CEF...");

        try
        {
            // Load CEF runtime
            CefRuntime.Load();
            _logger.Debug("CEF runtime loaded");

            // Find helper binary
            var helperPath = ResolveHelperPath(cefHelperPath);
            _logger.Info("Using CEF helper binary: {HelperPath}", helperPath);

            // Configure CEF settings for offscreen rendering
            var settings = new CefSettings
            {
                WindowlessRenderingEnabled = true,
                NoSandbox = true,
                ExternalMessagePump = true,
                MultiThreadedMessageLoop = false,
                BrowserSubprocessPath = helperPath,
                LogSeverity = CefLogSeverity.Warning,
            };

            // Create args from command line
            var args = Environment.GetCommandLineArgs();
            var mainArgs = new CefMainArgs(args);

            // Execute process - for main process this returns -1
            var exitCode = CefRuntime.ExecuteProcess(mainArgs, null, IntPtr.Zero);
            _logger.Debug("ExecuteProcess returned: {ExitCode}", exitCode);

            if (exitCode >= 0)
            {
                // This shouldn't happen if we're using BrowserSubprocessPath
                _logger.Warning("ExecuteProcess returned {ExitCode} - this should be a subprocess", exitCode);
            }

            // Initialize CEF
            CefRuntime.Initialize(mainArgs, settings, null, IntPtr.Zero);

            _initialized = true;
            _logger.Info("CEF initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize CEF");
            throw;
        }
    }

    /// <summary>
    /// Pumps CEF message loop. Must be called from Godot's _Process().
    /// </summary>
    public void DoMessageLoopWork()
    {
        if (!_initialized || _disposed)
        {
            return;
        }

        CefRuntime.DoMessageLoopWork();
    }

    /// <summary>
    /// Shuts down CEF. Must be called on application exit.
    /// </summary>
    public void Shutdown()
    {
        if (!_initialized || _disposed)
        {
            return;
        }

        using var scope = _logger.BeginScope("CefManager.Shutdown");
        _logger.Info("Shutting down CEF...");

        try
        {
            CefRuntime.Shutdown();
            _initialized = false;
            _logger.Info("CEF shutdown complete");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during CEF shutdown");
        }
    }

    /// <summary>
    /// Resolves the CEF helper binary path.
    /// Checks environment variable, then looks in executable directory.
    /// </summary>
    private string ResolveHelperPath(string? providedPath)
    {
        // First check provided path
        if (!string.IsNullOrEmpty(providedPath) && File.Exists(providedPath))
        {
            return providedPath;
        }

        // Check environment variable
        var envPath = Environment.GetEnvironmentVariable("CEF_HELPER_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }
        else if (!string.IsNullOrEmpty(envPath))
        {
            _logger.Warning("CEF_HELPER_PATH set but file not found: {Path}", envPath);
        }

        // Look in executable directory
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var helperNames = new[]
        {
            "CefHelper",
            "CefHelper.exe",
            "cef-helper",
            "cef-helper.exe",
        };

        foreach (var name in helperNames)
        {
            var path = Path.Combine(exeDir, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Throw if not found
        throw new FileNotFoundException(
            "CEF helper binary not found. Ensure CefHelper is in the same directory " +
            "as the main executable, or set CEF_HELPER_PATH environment variable."
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
        _instance = null;
    }
}
