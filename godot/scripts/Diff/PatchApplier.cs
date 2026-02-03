// Patch Applier - Apply Patches to Assets
//
// Takes a patch set and applies it to a target asset to create an updated mod.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Diff;

/// <summary>
/// Result of applying patches to an asset.
/// </summary>
/// <param name="Applied">Number of patches successfully applied</param>
/// <param name="Skipped">Number of patches skipped (e.g., requiring review)</param>
/// <param name="Failed">Number of patches that failed to apply</param>
/// <param name="Errors">Error messages for failed patches</param>
public record ApplyResult(
    [property: JsonPropertyName("applied")] int Applied,
    [property: JsonPropertyName("skipped")] int Skipped,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("errors")] string[] Errors
);

/// <summary>
/// Interface for patch application.
/// </summary>
public interface IPatchApplier
{
    /// <summary>
    /// Applies all non-review patches from a patch set to an asset.
    /// </summary>
    ApplyResult ApplyPatches(UAsset targetAsset, PatchSet patchSet);

    /// <summary>
    /// Applies a single patch to an asset.
    /// </summary>
    bool ApplyPatch(UAsset targetAsset, Patch patch, out string? error);

    /// <summary>
    /// Applies all safe (non-conflicting) changes from a three-way diff.
    /// </summary>
    ApplyResult ApplySafeChanges(UAsset targetAsset, ThreeWayDiffResult threeWayResult);

    /// <summary>
    /// Resolves a conflict by applying a specific resolution.
    /// </summary>
    bool ResolveConflict(
        UAsset targetAsset,
        DiffConflict conflict,
        string resolution,
        object? customValue,
        out string? error);
}

/// <summary>
/// Applies patches to UAsset files for mod porting.
/// </summary>
public sealed class PatchApplier : IPatchApplier
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Diff");

    private readonly IAppLogger _logger;

    public PatchApplier(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ApplyResult ApplyPatches(UAsset targetAsset, PatchSet patchSet)
    {
        using var activity = ActivitySource.StartActivity("ApplyPatches");

        ArgumentNullException.ThrowIfNull(targetAsset);
        ArgumentNullException.ThrowIfNull(patchSet);

        int applied = 0;
        int skipped = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (var patch in patchSet.Patches)
        {
            if (patch.RequiresReview)
            {
                skipped++;
                continue;
            }

            if (ApplyPatch(targetAsset, patch, out var error))
            {
                applied++;
            }
            else
            {
                failed++;
                if (error != null)
                {
                    errors.Add($"[{string.Join("/", patch.Path)}] {error}");
                }
            }
        }

        var result = new ApplyResult(applied, skipped, failed, errors.ToArray());

        _logger.Info("Applied patches: {Applied} success, {Skipped} skipped, {Failed} failed",
            applied, skipped, failed);

        activity?.SetStatus(failed > 0 ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        return result;
    }

    public bool ApplyPatch(UAsset targetAsset, Patch patch, out string? error)
    {
        error = null;

        try
        {
            return patch.Operation switch
            {
                PatchOperations.Set => ApplySetOperation(targetAsset, patch, out error),
                PatchOperations.Add => ApplyAddOperation(targetAsset, patch, out error),
                PatchOperations.Remove => ApplyRemoveOperation(targetAsset, patch, out error),
                PatchOperations.Rename => ApplyRenameOperation(targetAsset, patch, out error),
                _ => SetError(out error, $"Unknown operation: {patch.Operation}")
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.Error(ex, "Failed to apply patch at {Path}", string.Join("/", patch.Path));
            return false;
        }
    }

    public ApplyResult ApplySafeChanges(UAsset targetAsset, ThreeWayDiffResult threeWayResult)
    {
        using var activity = ActivitySource.StartActivity("ApplySafeChanges");

        ArgumentNullException.ThrowIfNull(targetAsset);
        ArgumentNullException.ThrowIfNull(threeWayResult);

        int applied = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (var change in threeWayResult.SafeToApply)
        {
            var patch = CreatePatchFromChange(change);

            if (ApplyPatch(targetAsset, patch, out var error))
            {
                applied++;
            }
            else
            {
                failed++;
                if (error != null)
                {
                    errors.Add($"[{string.Join("/", change.Path)}] {error}");
                }
            }
        }

        var result = new ApplyResult(applied, 0, failed, errors.ToArray());

        _logger.Info("Applied safe changes: {Applied} success, {Failed} failed", applied, failed);

        activity?.SetStatus(failed > 0 ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        return result;
    }

    public bool ResolveConflict(
        UAsset targetAsset,
        DiffConflict conflict,
        string resolution,
        object? customValue,
        out string? error)
    {
        error = null;

        try
        {
            object? valueToApply = resolution switch
            {
                "keep_game" => conflict.GameValue,
                "keep_mod" => conflict.ModValue,
                "custom" => customValue,
                _ => null
            };

            if (valueToApply == null && resolution != "keep_game" && resolution != "keep_mod")
            {
                error = $"Unknown resolution: {resolution}";
                return false;
            }

            var patch = new Patch(
                Path: conflict.Path,
                Operation: PatchOperations.Set,
                Value: valueToApply,
                RequiresReview: false,
                OriginalValue: conflict.OriginalValue,
                Reason: $"Resolved: {resolution}"
            );

            return ApplyPatch(targetAsset, patch, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.Error(ex, "Failed to resolve conflict at {Path}", string.Join("/", conflict.Path));
            return false;
        }
    }

    private bool ApplySetOperation(UAsset asset, Patch patch, out string? error)
    {
        error = null;

        var (export, property, parentPath) = NavigateToProperty(asset, patch.Path);

        if (property == null)
        {
            error = "Property not found";
            return false;
        }

        return SetPropertyValue(property, patch.Value, out error);
    }

    private bool ApplyAddOperation(UAsset asset, Patch patch, out string? error)
    {
        error = null;

        if (patch.Path.Length < 2)
        {
            error = "Invalid path for add operation";
            return false;
        }

        // Navigate to parent
        var parentPath = patch.Path.Take(patch.Path.Length - 1).ToArray();
        var (export, parentProperty, _) = NavigateToProperty(asset, parentPath);

        if (parentProperty == null && export == null)
        {
            error = "Parent not found";
            return false;
        }

        // Add to parent based on type
        if (parentProperty is ArrayPropertyData arrayProp)
        {
            return AddToArray(arrayProp, patch.Value, out error);
        }

        if (parentProperty is StructPropertyData structProp)
        {
            var propName = patch.Path[^1];
            return AddToStruct(asset, structProp, propName, patch.Value, out error);
        }

        if (export is NormalExport normalExport)
        {
            var propName = patch.Path[^1];
            return AddToExport(asset, normalExport, propName, patch.Value, out error);
        }

        error = "Cannot add to this type of property";
        return false;
    }

    private bool ApplyRemoveOperation(UAsset asset, Patch patch, out string? error)
    {
        error = null;

        if (patch.Path.Length < 2)
        {
            error = "Invalid path for remove operation";
            return false;
        }

        var parentPath = patch.Path.Take(patch.Path.Length - 1).ToArray();
        var (export, parentProperty, _) = NavigateToProperty(asset, parentPath);

        if (parentProperty == null && export == null)
        {
            error = "Parent not found";
            return false;
        }

        var targetName = patch.Path[^1];

        if (parentProperty is ArrayPropertyData arrayProp)
        {
            return RemoveFromArray(arrayProp, targetName, out error);
        }

        if (parentProperty is StructPropertyData structProp)
        {
            return RemoveFromStruct(structProp, targetName, out error);
        }

        if (export is NormalExport normalExport)
        {
            return RemoveFromExport(normalExport, targetName, out error);
        }

        error = "Cannot remove from this type of property";
        return false;
    }

    private bool ApplyRenameOperation(UAsset asset, Patch patch, out string? error)
    {
        error = null;

        var (export, property, _) = NavigateToProperty(asset, patch.Path);

        if (property == null)
        {
            error = "Property not found";
            return false;
        }

        if (patch.Value is not string newName)
        {
            error = "New name must be a string";
            return false;
        }

        property.Name = new FName(asset, newName);
        _logger.Debug("Renamed property to: {NewName}", newName);
        return true;
    }

    private (NormalExport? export, PropertyData? property, string[] remainingPath) NavigateToProperty(
        UAsset asset,
        string[] path)
    {
        if (path.Length == 0)
        {
            return (null, null, Array.Empty<string>());
        }

        // Parse export index from path[0] (e.g., "Export[0]")
        if (!path[0].StartsWith("Export["))
        {
            return (null, null, path);
        }

        var indexStr = path[0].AsSpan(7, path[0].Length - 8);
        if (!int.TryParse(indexStr, out int exportIndex) ||
            exportIndex < 0 ||
            exportIndex >= asset.Exports.Count)
        {
            return (null, null, path);
        }

        var export = asset.Exports[exportIndex];
        if (export is not NormalExport normalExport)
        {
            return (null, null, path.Skip(1).ToArray());
        }

        if (path.Length == 1)
        {
            return (normalExport, null, Array.Empty<string>());
        }

        // Navigate through properties
        PropertyData? currentProperty = null;
        IList<PropertyData> currentList = normalExport.Data;

        for (int i = 1; i < path.Length; i++)
        {
            var segment = path[i];

            // Array index
            if (segment.StartsWith("[") && segment.EndsWith("]"))
            {
                if (currentProperty is not ArrayPropertyData arrayProp)
                {
                    return (normalExport, currentProperty, path.Skip(i).ToArray());
                }

                var indexPart = segment.AsSpan(1, segment.Length - 2);
                if (!int.TryParse(indexPart, out int arrayIndex) ||
                    arrayIndex < 0 ||
                    arrayIndex >= arrayProp.Value.Length)
                {
                    return (normalExport, currentProperty, path.Skip(i).ToArray());
                }

                currentProperty = arrayProp.Value[arrayIndex];

                if (currentProperty is StructPropertyData structProp)
                {
                    currentList = structProp.Value;
                }
            }
            else
            {
                // Property name
                var found = currentList.FirstOrDefault(p => p.Name.Value.Value == segment);
                if (found == null)
                {
                    return (normalExport, currentProperty, path.Skip(i).ToArray());
                }

                currentProperty = found;

                if (currentProperty is StructPropertyData structProp)
                {
                    currentList = structProp.Value;
                }
                else if (currentProperty is ArrayPropertyData)
                {
                    // Next segment should be array index
                    continue;
                }
            }
        }

        return (normalExport, currentProperty, Array.Empty<string>());
    }

    private static bool SetPropertyValue(PropertyData property, object? value, out string? error)
    {
        error = null;

        try
        {
            switch (property)
            {
                case IntPropertyData intProp:
                    intProp.Value = Convert.ToInt32(value);
                    break;
                case FloatPropertyData floatProp:
                    floatProp.Value = Convert.ToSingle(value);
                    break;
                case DoublePropertyData doubleProp:
                    doubleProp.Value = Convert.ToDouble(value);
                    break;
                case BoolPropertyData boolProp:
                    boolProp.Value = Convert.ToBoolean(value);
                    break;
                case StrPropertyData strProp:
                    strProp.Value = new FString(value?.ToString());
                    break;
                case Int8PropertyData int8Prop:
                    int8Prop.Value = Convert.ToSByte(value);
                    break;
                case Int16PropertyData int16Prop:
                    int16Prop.Value = Convert.ToInt16(value);
                    break;
                case Int64PropertyData int64Prop:
                    int64Prop.Value = Convert.ToInt64(value);
                    break;
                case UInt16PropertyData uint16Prop:
                    uint16Prop.Value = Convert.ToUInt16(value);
                    break;
                case UInt32PropertyData uint32Prop:
                    uint32Prop.Value = Convert.ToUInt32(value);
                    break;
                case UInt64PropertyData uint64Prop:
                    uint64Prop.Value = Convert.ToUInt64(value);
                    break;
                case BytePropertyData byteProp:
                    if (byteProp.ByteType == BytePropertyType.Byte)
                    {
                        byteProp.Value = Convert.ToByte(value);
                    }
                    break;
                default:
                    error = $"Cannot set value for property type: {property.PropertyType.Value.Value}";
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Value conversion failed: {ex.Message}";
            return false;
        }
    }

    private static bool AddToArray(ArrayPropertyData arrayProp, object? value, out string? error)
    {
        error = null;

        // For now, just log that we would add to the array
        // Full implementation would need to construct the appropriate PropertyData type
        error = "Array element addition not yet implemented";
        return false;
    }

    private static bool AddToStruct(UAsset asset, StructPropertyData structProp, string name, object? value, out string? error)
    {
        error = null;

        // Check if property already exists
        if (structProp.Value.Any(p => p.Name.Value.Value == name))
        {
            error = $"Property '{name}' already exists in struct";
            return false;
        }

        // For now, just log that we would add to the struct
        error = "Struct property addition not yet implemented";
        return false;
    }

    private static bool AddToExport(UAsset asset, NormalExport export, string name, object? value, out string? error)
    {
        error = null;

        // Check if property already exists
        if (export.Data.Any(p => p.Name.Value.Value == name))
        {
            error = $"Property '{name}' already exists in export";
            return false;
        }

        // For now, just log that we would add to the export
        error = "Export property addition not yet implemented";
        return false;
    }

    private static bool RemoveFromArray(ArrayPropertyData arrayProp, string indexStr, out string? error)
    {
        error = null;

        // Parse index (remove brackets if present)
        var cleanIndex = indexStr.TrimStart('[').TrimEnd(']');
        if (!int.TryParse(cleanIndex, out int index))
        {
            error = $"Invalid array index: {indexStr}";
            return false;
        }

        if (index < 0 || index >= arrayProp.Value.Length)
        {
            error = $"Array index out of range: {index}";
            return false;
        }

        var newArray = new PropertyData[arrayProp.Value.Length - 1];
        Array.Copy(arrayProp.Value, 0, newArray, 0, index);
        Array.Copy(arrayProp.Value, index + 1, newArray, index, arrayProp.Value.Length - index - 1);
        arrayProp.Value = newArray;

        return true;
    }

    private static bool RemoveFromStruct(StructPropertyData structProp, string name, out string? error)
    {
        error = null;

        var property = structProp.Value.FirstOrDefault(p => p.Name.Value.Value == name);
        if (property == null)
        {
            error = $"Property '{name}' not found in struct";
            return false;
        }

        structProp.Value.Remove(property);
        return true;
    }

    private static bool RemoveFromExport(NormalExport export, string name, out string? error)
    {
        error = null;

        var property = export.Data.FirstOrDefault(p => p.Name.Value.Value == name);
        if (property == null)
        {
            error = $"Property '{name}' not found in export";
            return false;
        }

        export.Data.Remove(property);
        return true;
    }

    private static Patch CreatePatchFromChange(DiffChange change)
    {
        var operation = change.ChangeType switch
        {
            DiffChangeTypes.Added => PatchOperations.Add,
            DiffChangeTypes.Removed => PatchOperations.Remove,
            DiffChangeTypes.Modified => PatchOperations.Set,
            DiffChangeTypes.Renamed => PatchOperations.Rename,
            _ => PatchOperations.Set
        };

        return new Patch(
            Path: change.Path,
            Operation: operation,
            Value: change.NewValue,
            RequiresReview: false,
            OriginalValue: change.OldValue
        );
    }

    private static bool SetError(out string? error, string message)
    {
        error = message;
        return false;
    }
}
