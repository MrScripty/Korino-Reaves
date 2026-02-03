// Diff Plugin - Semantic Kernel functions for asset comparison
//
// Exposes diff operations to the AI agent for mod porting workflows.

using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for asset diff and conflict detection.
/// Accepts the diff engine as an object to avoid circular dependencies
/// with the Diff module (which is built by a separate agent).
/// </summary>
public sealed class DiffPlugin
{
    private readonly object _diffEngine;

    public DiffPlugin(object diffEngine)
    {
        _diffEngine = diffEngine;
    }

    [KernelFunction("compare_assets")]
    [Description("Compares two asset files and returns their differences. Useful for detecting what changed between game versions.")]
    public string CompareAssets(
        [Description("Path to the original (base) asset file")] string originalPath,
        [Description("Path to the updated asset file")] string updatedPath)
    {
        // The DiffEngine is created by the Diff agent (05-diff).
        // We use reflection to call its methods to avoid compile-time coupling.
        var method = _diffEngine.GetType().GetMethod("ComputeDiff");
        if (method == null)
        {
            return JsonSerializer.Serialize(new { error = "DiffEngine.ComputeDiff not available" });
        }

        var result = method.Invoke(_diffEngine, new object[] { originalPath, updatedPath });
        return JsonSerializer.Serialize(result);
    }

    [KernelFunction("detect_conflicts")]
    [Description("Performs a three-way diff to detect conflicts between a game update and mod changes. " +
        "Compares original -> updated (game changes) and original -> modded (mod changes).")]
    public string DetectConflicts(
        [Description("Path to the original game asset")] string originalPath,
        [Description("Path to the updated game asset")] string updatedPath,
        [Description("Path to the modded asset")] string modPath)
    {
        var method = _diffEngine.GetType().GetMethod("ComputeThreeWayDiff");
        if (method == null)
        {
            return JsonSerializer.Serialize(new { error = "DiffEngine.ComputeThreeWayDiff not available" });
        }

        var result = method.Invoke(_diffEngine, new object[] { originalPath, updatedPath, modPath });
        return JsonSerializer.Serialize(result);
    }
}
