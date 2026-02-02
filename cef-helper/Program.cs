// CEF subprocess helper binary
//
// This is a minimal executable used by CEF for its subprocess architecture.
// CEF spawns multiple processes (render, GPU, utility) and by default uses
// the main executable with different command line arguments.
//
// By providing a separate helper binary, we avoid:
// - The main app's initialization code running in subprocesses
// - Godot initialization conflicts
// - Runaway subprocess spawning issues
//
// This binary does one thing: calls CefRuntime.ExecuteProcess() and exits.

using System;
using Xilium.CefGlue;

namespace UAssetViewer.CefHelper;

/// <summary>
/// CEF subprocess helper entry point.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            // Load CEF runtime
            CefRuntime.Load();

            // Create the main args from command line
            var mainArgs = new CefMainArgs(args);

            // Execute the CEF subprocess logic
            // CEF will determine what type of subprocess this is from command line args
            var exitCode = CefRuntime.ExecuteProcess(mainArgs, null, IntPtr.Zero);

            // exitCode >= 0 means this was a subprocess and CEF handled it
            // exitCode < 0 means this is the browser process (shouldn't happen for helper)
            return exitCode >= 0 ? exitCode : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CefHelper fatal error: {ex.Message}");
            return 1;
        }
    }
}
