// Conflict Detector - Three-Way Merge Conflict Detection
//
// Analyzes game changes (original -> updated) and mod changes (original -> modded)
// to identify conflicts where both game and mod changed the same property.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UAssetAPI;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Diff;

/// <summary>
/// Result of conflict detection analysis.
/// </summary>
public sealed class ConflictResult
{
    /// <summary>
    /// Mod changes that don't conflict with game changes.
    /// These can be safely auto-applied to the new version.
    /// </summary>
    public DiffChange[] NonConflicting { get; }

    /// <summary>
    /// Changes where both game and mod modified the same property.
    /// These require manual resolution.
    /// </summary>
    public DiffConflict[] Conflicting { get; }

    /// <summary>
    /// Structural issues where game removed something the mod depends on.
    /// These may break the mod and require investigation.
    /// </summary>
    public DiffChange[] Structural { get; }

    public ConflictResult(
        DiffChange[] nonConflicting,
        DiffConflict[] conflicting,
        DiffChange[] structural)
    {
        NonConflicting = nonConflicting;
        Conflicting = conflicting;
        Structural = structural;
    }
}

/// <summary>
/// Interface for conflict detection between game and mod changes.
/// </summary>
public interface IConflictDetector
{
    /// <summary>
    /// Detects conflicts between game changes and mod changes.
    /// </summary>
    /// <param name="gameChanges">Changes from original to updated game version</param>
    /// <param name="modChanges">Changes from original to modded version</param>
    /// <returns>Categorized changes and conflicts</returns>
    ConflictResult DetectConflicts(DiffResult gameChanges, DiffResult modChanges);

    /// <summary>
    /// Performs a full three-way diff operation.
    /// </summary>
    ThreeWayDiffResult PerformThreeWayDiff(
        UAsset original,
        UAsset updated,
        UAsset modded);
}

/// <summary>
/// Detects conflicts between game updates and mod changes for mod porting.
/// </summary>
public sealed class ConflictDetector : IConflictDetector
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Diff");

    private readonly IAppLogger _logger;
    private readonly IDiffEngine _diffEngine;

    public ConflictDetector(IAppLogger logger, IDiffEngine diffEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));
    }

    public ConflictResult DetectConflicts(DiffResult gameChanges, DiffResult modChanges)
    {
        using var activity = ActivitySource.StartActivity("DetectConflicts");

        ArgumentNullException.ThrowIfNull(gameChanges);
        ArgumentNullException.ThrowIfNull(modChanges);

        _logger.Info("Detecting conflicts between game and mod changes");

        // Build lookup of game changes by path
        var gameChangePaths = new Dictionary<string, DiffChange>();
        foreach (var change in gameChanges.Changes)
        {
            var pathKey = GetPathKey(change.Path);
            gameChangePaths[pathKey] = change;
        }

        var nonConflicting = new List<DiffChange>();
        var conflicting = new List<DiffConflict>();
        var structural = new List<DiffChange>();

        foreach (var modChange in modChanges.Changes)
        {
            var pathKey = GetPathKey(modChange.Path);

            if (gameChangePaths.TryGetValue(pathKey, out var gameChange))
            {
                // Both game and mod changed this path
                var conflict = AnalyzeConflict(modChange, gameChange);
                if (conflict != null)
                {
                    conflicting.Add(conflict);
                }
                else
                {
                    // Same change made by both - no conflict
                    nonConflicting.Add(modChange);
                }
            }
            else
            {
                // Check for structural issues
                if (IsStructuralIssue(modChange, gameChangePaths))
                {
                    structural.Add(modChange);
                }
                else
                {
                    // Mod changed something game didn't touch
                    nonConflicting.Add(modChange);
                }
            }
        }

        var result = new ConflictResult(
            nonConflicting.ToArray(),
            conflicting.ToArray(),
            structural.ToArray()
        );

        _logger.Info("Conflict detection complete: {Safe} safe, {Conflicts} conflicts, {Structural} structural",
            result.NonConflicting.Length, result.Conflicting.Length, result.Structural.Length);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return result;
    }

    public ThreeWayDiffResult PerformThreeWayDiff(
        UAsset original,
        UAsset updated,
        UAsset modded)
    {
        using var activity = ActivitySource.StartActivity("ThreeWayDiff");

        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentNullException.ThrowIfNull(modded);

        _logger.Info("Performing three-way diff for mod porting");

        // Compute both diffs
        var gameChanges = _diffEngine.ComputeDiff(original, updated);
        var modChanges = _diffEngine.ComputeDiff(original, modded);

        // Detect conflicts
        var conflictResult = DetectConflicts(gameChanges, modChanges);

        var result = new ThreeWayDiffResult(
            OriginalVersion: original.FilePath ?? "original",
            UpdatedVersion: updated.FilePath ?? "updated",
            ModdedVersion: modded.FilePath ?? "modded",
            GameChanges: gameChanges.Changes,
            ModChanges: modChanges.Changes,
            Conflicts: conflictResult.Conflicting,
            SafeToApply: conflictResult.NonConflicting
        );

        _logger.Info("Three-way diff complete: {GameChanges} game, {ModChanges} mod, {Conflicts} conflicts, {Safe} safe",
            result.GameChanges.Length, result.ModChanges.Length,
            result.Conflicts.Length, result.SafeToApply.Length);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return result;
    }

    private DiffConflict? AnalyzeConflict(DiffChange modChange, DiffChange gameChange)
    {
        // If both made the same change, no conflict
        if (ChangesAreEquivalent(modChange, gameChange))
        {
            return null;
        }

        // Determine suggested resolution
        string? suggestedResolution = DetermineSuggestedResolution(modChange, gameChange);

        return new DiffConflict(
            Path: modChange.Path,
            OriginalValue: modChange.OldValue,
            GameValue: gameChange.NewValue,
            ModValue: modChange.NewValue,
            SuggestedResolution: suggestedResolution
        );
    }

    private static bool ChangesAreEquivalent(DiffChange a, DiffChange b)
    {
        // Same type and same new value
        if (a.ChangeType != b.ChangeType)
        {
            return false;
        }

        // Compare new values
        return ValuesEqual(a.NewValue, b.NewValue);
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Equals(b) || a.ToString() == b.ToString();
    }

    private static string? DetermineSuggestedResolution(DiffChange modChange, DiffChange gameChange)
    {
        // If game removed and mod modified - suggest keeping mod if possible
        if (gameChange.ChangeType == DiffChangeTypes.Removed &&
            modChange.ChangeType == DiffChangeTypes.Modified)
        {
            return null; // Requires manual intervention
        }

        // If game modified and mod removed - suggest keeping game
        if (gameChange.ChangeType == DiffChangeTypes.Modified &&
            modChange.ChangeType == DiffChangeTypes.Removed)
        {
            return "keep_game";
        }

        // Both modified - no automatic suggestion
        if (gameChange.ChangeType == DiffChangeTypes.Modified &&
            modChange.ChangeType == DiffChangeTypes.Modified)
        {
            // Check if mod change is additive/compatible
            if (IsAdditiveChange(modChange))
            {
                return "merge";
            }
            return null;
        }

        return null;
    }

    private static bool IsAdditiveChange(DiffChange change)
    {
        // Changes that add information rather than replace it
        // For example, adding to an array rather than replacing values
        var path = change.Path;
        if (path.Length > 0 && path[^1].StartsWith("[") && path[^1].EndsWith("]"))
        {
            // Array element changes might be mergeable
            return change.ChangeType == DiffChangeTypes.Added;
        }
        return false;
    }

    private bool IsStructuralIssue(DiffChange modChange, Dictionary<string, DiffChange> gameChangePaths)
    {
        // Check if mod depends on something the game removed
        if (modChange.ChangeType == DiffChangeTypes.Modified)
        {
            // Check if any parent path was removed by the game
            var parentPath = modChange.Path.Take(modChange.Path.Length - 1).ToArray();
            while (parentPath.Length > 0)
            {
                var parentKey = GetPathKey(parentPath);
                if (gameChangePaths.TryGetValue(parentKey, out var gameChange))
                {
                    if (gameChange.ChangeType == DiffChangeTypes.Removed)
                    {
                        _logger.Warning("Structural issue: mod modified {Path} but game removed parent {Parent}",
                            string.Join("/", modChange.Path),
                            string.Join("/", parentPath));
                        return true;
                    }
                }
                parentPath = parentPath.Take(parentPath.Length - 1).ToArray();
            }
        }

        return false;
    }

    private static string GetPathKey(string[] path)
    {
        return string.Join("/", path);
    }
}
