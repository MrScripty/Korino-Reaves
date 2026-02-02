namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Type of change detected between two versions.
/// </summary>
public static class DiffChangeTypes
{
    public const string Added = "added";
    public const string Removed = "removed";
    public const string Modified = "modified";
    public const string Renamed = "renamed";
    public const string Moved = "moved";
}

/// <summary>
/// Represents a single difference between two asset versions.
/// </summary>
/// <param name="Path">Path to the changed element</param>
/// <param name="ChangeType">Type of change</param>
/// <param name="OldValue">Value in base version (null for 'added')</param>
/// <param name="NewValue">Value in target version (null for 'removed')</param>
/// <param name="Confidence">Confidence score for rename/move detection (0.0-1.0)</param>
/// <param name="OriginalPath">For renames: the original path</param>
public record DiffChange(
    [property: JsonPropertyName("path")] string[] Path,
    [property: JsonPropertyName("changeType")] string ChangeType,
    [property: JsonPropertyName("oldValue")] object? OldValue = null,
    [property: JsonPropertyName("newValue")] object? NewValue = null,
    [property: JsonPropertyName("confidence")] double? Confidence = null,
    [property: JsonPropertyName("originalPath")] string[]? OriginalPath = null
);

/// <summary>
/// Summary statistics for a diff operation.
/// </summary>
/// <param name="Added">Number of additions</param>
/// <param name="Removed">Number of removals</param>
/// <param name="Modified">Number of modifications</param>
/// <param name="Unchanged">Number of unchanged elements</param>
/// <param name="Renamed">Number of renames detected</param>
public record DiffSummary(
    [property: JsonPropertyName("added")] int Added,
    [property: JsonPropertyName("removed")] int Removed,
    [property: JsonPropertyName("modified")] int Modified,
    [property: JsonPropertyName("unchanged")] int Unchanged,
    [property: JsonPropertyName("renamed")] int? Renamed = null
);

/// <summary>
/// Complete result of comparing two assets.
/// </summary>
/// <param name="BaseVersion">Identifier for base (original) version</param>
/// <param name="TargetVersion">Identifier for target (new) version</param>
/// <param name="Changes">List of all changes detected</param>
/// <param name="Summary">Aggregated statistics</param>
public record DiffResult(
    [property: JsonPropertyName("baseVersion")] string BaseVersion,
    [property: JsonPropertyName("targetVersion")] string TargetVersion,
    [property: JsonPropertyName("changes")] DiffChange[] Changes,
    [property: JsonPropertyName("summary")] DiffSummary Summary
);

/// <summary>
/// A conflict where both game update and mod changed the same property.
/// </summary>
/// <param name="Path">Path to the conflicting element</param>
/// <param name="OriginalValue">Original value before any changes</param>
/// <param name="GameValue">Value in updated game version</param>
/// <param name="ModValue">Value in modded version</param>
/// <param name="SuggestedResolution">Suggested resolution (if determinable)</param>
public record DiffConflict(
    [property: JsonPropertyName("path")] string[] Path,
    [property: JsonPropertyName("originalValue")] object? OriginalValue,
    [property: JsonPropertyName("gameValue")] object? GameValue,
    [property: JsonPropertyName("modValue")] object? ModValue,
    [property: JsonPropertyName("suggestedResolution")] string? SuggestedResolution = null
);

/// <summary>
/// Three-way diff result for mod porting.
/// Compares: Original -> Updated (game changes) and Original -> Modded (mod changes)
/// </summary>
/// <param name="OriginalVersion">Original game version identifier</param>
/// <param name="UpdatedVersion">Updated game version identifier</param>
/// <param name="ModdedVersion">Modded version identifier</param>
/// <param name="GameChanges">Changes the game made (original -> updated)</param>
/// <param name="ModChanges">Changes the mod made (original -> modded)</param>
/// <param name="Conflicts">Conflicts where both game and mod changed the same thing</param>
/// <param name="SafeToApply">Non-conflicting mod changes that can be auto-applied</param>
public record ThreeWayDiffResult(
    [property: JsonPropertyName("originalVersion")] string OriginalVersion,
    [property: JsonPropertyName("updatedVersion")] string UpdatedVersion,
    [property: JsonPropertyName("moddedVersion")] string ModdedVersion,
    [property: JsonPropertyName("gameChanges")] DiffChange[] GameChanges,
    [property: JsonPropertyName("modChanges")] DiffChange[] ModChanges,
    [property: JsonPropertyName("conflicts")] DiffConflict[] Conflicts,
    [property: JsonPropertyName("safeToApply")] DiffChange[] SafeToApply
);
