// Thread-safe shared state between CEF handlers and Godot
//
// This class manages the framebuffer and dirty flag for zero-copy sharing
// between the CEF render handler and Godot's texture update loop.

using System;
using System.Threading;

namespace UAssetViewer.Cef;

/// <summary>
/// Thread-safe shared state for CEF offscreen rendering.
/// Holds the framebuffer, dimensions, and dirty flag for efficient updates.
/// </summary>
public sealed class SharedState : IDisposable
{
    private readonly object _framebufferLock = new();
    private byte[]? _framebuffer;
    private int _width;
    private int _height;
    private int _dirty;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the requested viewport size.
    /// CEF will render at this size on the next paint.
    /// </summary>
    public (int Width, int Height) ViewportSize { get; set; } = (1920, 1080);

    /// <summary>
    /// Checks if the framebuffer has been updated since the last capture.
    /// </summary>
    public bool IsDirty => Interlocked.CompareExchange(ref _dirty, 0, 0) == 1;

    /// <summary>
    /// Gets the current framebuffer dimensions.
    /// </summary>
    public (int Width, int Height) FramebufferSize
    {
        get
        {
            lock (_framebufferLock)
            {
                return (_width, _height);
            }
        }
    }

    /// <summary>
    /// Checks if a framebuffer is available.
    /// </summary>
    public bool HasFramebuffer
    {
        get
        {
            lock (_framebufferLock)
            {
                return _framebuffer != null;
            }
        }
    }

    /// <summary>
    /// Updates the framebuffer from CEF paint callback.
    /// Called from the CEF render handler on paint events.
    /// </summary>
    /// <param name="buffer">BGRA pixel data</param>
    /// <param name="width">Buffer width in pixels</param>
    /// <param name="height">Buffer height in pixels</param>
    public void UpdateFramebuffer(ReadOnlySpan<byte> buffer, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_framebufferLock)
        {
            var requiredSize = width * height * 4;

            // Resize buffer if needed
            if (_framebuffer == null || _framebuffer.Length != requiredSize)
            {
                _framebuffer = new byte[requiredSize];
            }

            // Copy the BGRA buffer
            buffer.CopyTo(_framebuffer.AsSpan());
            _width = width;
            _height = height;
        }

        // Mark as dirty
        Interlocked.Exchange(ref _dirty, 1);
    }

    /// <summary>
    /// Captures the framebuffer if it has been updated.
    /// Returns null if not dirty or no framebuffer available.
    /// </summary>
    /// <returns>Tuple of (BGRA data, width, height) or null</returns>
    public (byte[] Data, int Width, int Height)? CaptureIfDirty()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check and clear dirty flag atomically
        if (Interlocked.CompareExchange(ref _dirty, 0, 1) != 1)
        {
            return null;
        }

        lock (_framebufferLock)
        {
            if (_framebuffer == null)
            {
                return null;
            }

            // Copy the buffer for the caller
            // This is necessary because we need to release the lock
            var copy = new byte[_framebuffer.Length];
            _framebuffer.AsSpan().CopyTo(copy);
            return (copy, _width, _height);
        }
    }

    /// <summary>
    /// Captures the framebuffer unconditionally.
    /// Useful for debugging or when you need the current state regardless of dirty flag.
    /// </summary>
    /// <returns>Tuple of (BGRA data, width, height) or null if no buffer</returns>
    public (byte[] Data, int Width, int Height)? CaptureUnconditional()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_framebufferLock)
        {
            if (_framebuffer == null)
            {
                return null;
            }

            var copy = new byte[_framebuffer.Length];
            _framebuffer.AsSpan().CopyTo(copy);
            return (copy, _width, _height);
        }
    }

    /// <summary>
    /// Marks the framebuffer as dirty.
    /// Useful when the UI sends a dirty notification.
    /// </summary>
    public void MarkDirty()
    {
        Interlocked.Exchange(ref _dirty, 1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_framebufferLock)
        {
            _framebuffer = null;
        }
    }
}
