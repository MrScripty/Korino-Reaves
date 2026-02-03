// Patch Generator - Generate Mod Update Patches
//
// Converts diff changes into patches that can be applied to update a mod
// to work with a new game version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Diff;

/// <summary>
/// Type of patch operation.
/// </summary>
public static class PatchOperations
{
    public const string Set = "set";
    public const string Add = "add";
    public const string Remove = "remove";
    public const string Rename = "rename";
}

/// <summary>
/// Represents a single patch operation to apply to an asset.
/// </summary>
/// <param name="Path">Path to the property to patch</param>
/// <param name="Operation">Type of operation</param>
/// <param name="Value">Value to set/add (null for remove)</param>
/// <param name="RequiresReview">Whether this patch needs manual review</param>
/// <param name="OriginalValue">Original value before the change</param>
/// <param name="Reason">Reason for this patch</param>
public record Patch(
    [property: JsonPropertyName("path")] string[] Path,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("value")] object? Value,
    [property: JsonPropertyName("requiresReview")] bool RequiresReview,
    [property: JsonPropertyName("originalValue")] object? OriginalValue = null,
    [property: JsonPropertyName("reason")] string? Reason = null
);

/// <summary>
/// A set of patches with metadata.
/// </summary>
public record PatchSet(
    [property: JsonPropertyName("sourceVersion")] string SourceVersion,
    [property: JsonPropertyName("targetVersion")] string TargetVersion,
    [property: JsonPropertyName("patches")] Patch[] Patches,
    [property: JsonPropertyName("autoApplyCount")] int AutoApplyCount,
    [property: JsonPropertyName("reviewCount")] int ReviewCount
);

/// <summary>
/// Interface for patch generation.
/// </summary>
public interface IPatchGenerator
{
    /// <summary>
    /// Generates patches from mod changes and conflict information.
    /// </summary>
    PatchSet GeneratePatches(DiffChange[] modChanges, ConflictResult conflicts);

    /// <summary>
    /// Generates patches for a three-way diff result.
    /// </summary>
    PatchSet GeneratePatchesFromThreeWay(ThreeWayDiffResult threeWayResult);
}

/// <summary>
/// Generates patches for mod porting from diff changes.
/// </summary>
public sealed class PatchGenerator : IPatchGenerator
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Diff");

    private readonly IAppLogger _logger;

    public PatchGenerator(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PatchSet GeneratePatches(DiffChange[] modChanges, ConflictResult conflicts)
    {
        using var activity = ActivitySource.StartActivity("GeneratePatches");

        ArgumentNullException.ThrowIfNull(modChanges);
        ArgumentNullException.ThrowIfNull(conflicts);

        var patches = new List<Patch>();

        // Generate patches for non-conflicting changes
        foreach (var change in conflicts.NonConflicting)
        {
            var patch = CreatePatchFromChange(change, requiresReview: false);
            if (patch != null)
            {
                patches.Add(patch);
            }
        }

        // Generate patches for conflicting changes (require review)
        foreach (var conflict in conflicts.Conflicting)
        {
            var patch = CreatePatchFromConflict(conflict);
            patches.Add(patch);
        }

        // Generate patches for structural issues (require review)
        foreach (var structural in conflicts.Structural)
        {
            var patch = CreatePatchFromChange(structural, requiresReview: true);
            if (patch != null)
            {
                patch = patch with { Reason = "Structural issue: game modified parent structure" };
                patches.Add(patch);
            }
        }

        var patchSet = new PatchSet(
            SourceVersion: "modded",
            TargetVersion: "updated",
            Patches: patches.ToArray(),
            AutoApplyCount: patches.Count(p => !p.RequiresReview),
            ReviewCount: patches.Count(p => p.RequiresReview)
        );

        _logger.Info("Generated {Total} patches: {Auto} auto-apply, {Review} require review",
            patches.Count, patchSet.AutoApplyCount, patchSet.ReviewCount);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return patchSet;
    }

    public PatchSet GeneratePatchesFromThreeWay(ThreeWayDiffResult threeWayResult)
    {
        using var activity = ActivitySource.StartActivity("GeneratePatchesFromThreeWay");

        ArgumentNullException.ThrowIfNull(threeWayResult);

        var patches = new List<Patch>();

        // Safe changes can be auto-applied
        foreach (var change in threeWayResult.SafeToApply)
        {
            var patch = CreatePatchFromChange(change, requiresReview: false);
            if (patch != null)
            {
                patches.Add(patch);
            }
        }

        // Conflicts require review
        foreach (var conflict in threeWayResult.Conflicts)
        {
            var patch = CreatePatchFromConflict(conflict);
            patches.Add(patch);
        }

        var patchSet = new PatchSet(
            SourceVersion: threeWayResult.ModdedVersion,
            TargetVersion: threeWayResult.UpdatedVersion,
            Patches: patches.ToArray(),
            AutoApplyCount: patches.Count(p => !p.RequiresReview),
            ReviewCount: patches.Count(p => p.RequiresReview)
        );

        _logger.Info("Generated {Total} patches from three-way diff: {Auto} auto-apply, {Review} require review",
            patches.Count, patchSet.AutoApplyCount, patchSet.ReviewCount);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return patchSet;
    }

    private static Patch? CreatePatchFromChange(DiffChange change, bool requiresReview)
    {
        var operation = change.ChangeType switch
        {
            DiffChangeTypes.Added => PatchOperations.Add,
            DiffChangeTypes.Removed => PatchOperations.Remove,
            DiffChangeTypes.Modified => PatchOperations.Set,
            DiffChangeTypes.Renamed => PatchOperations.Rename,
            _ => null
        };

        if (operation == null)
        {
            return null;
        }

        return new Patch(
            Path: change.Path,
            Operation: operation,
            Value: change.NewValue,
            RequiresReview: requiresReview,
            OriginalValue: change.OldValue,
            Reason: requiresReview ? GetReasonForChange(change) : null
        );
    }

    private static Patch CreatePatchFromConflict(DiffConflict conflict)
    {
        // For conflicts, we default to the mod value but mark as requiring review
        return new Patch(
            Path: conflict.Path,
            Operation: PatchOperations.Set,
            Value: conflict.ModValue,
            RequiresReview: true,
            OriginalValue: conflict.OriginalValue,
            Reason: BuildConflictReason(conflict)
        );
    }

    private static string GetReasonForChange(DiffChange change)
    {
        return change.ChangeType switch
        {
            DiffChangeTypes.Added => $"Mod added: {FormatValue(change.NewValue)}",
            DiffChangeTypes.Removed => $"Mod removed: {FormatValue(change.OldValue)}",
            DiffChangeTypes.Modified => $"Mod changed: {FormatValue(change.OldValue)} -> {FormatValue(change.NewValue)}",
            DiffChangeTypes.Renamed => $"Mod renamed: {FormatValue(change.OldValue)} -> {FormatValue(change.NewValue)}",
            _ => "Unknown change"
        };
    }

    private static string BuildConflictReason(DiffConflict conflict)
    {
        return $"CONFLICT: Game changed to {FormatValue(conflict.GameValue)}, " +
               $"mod changed to {FormatValue(conflict.ModValue)} " +
               $"(was {FormatValue(conflict.OriginalValue)})";
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "null";
        if (value is string s) return $"\"{TruncateString(s, 30)}\"";
        return TruncateString(value.ToString() ?? "null", 30);
    }

    private static string TruncateString(string value, int maxLen)
    {
        if (value.Length <= maxLen) return value;
        return value.Substring(0, maxLen - 3) + "...";
    }
}
