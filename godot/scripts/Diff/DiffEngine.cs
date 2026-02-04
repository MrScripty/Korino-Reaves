// Diff Engine - Core Asset Comparison
//
// Compares two UAsset files to detect differences in exports, properties, and values.
// Used for mod porting workflows where users need to understand what changed between versions.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Diff;

/// <summary>
/// Interface for the diff engine.
/// </summary>
public interface IDiffEngine
{
    /// <summary>
    /// Computes the differences between two assets.
    /// </summary>
    /// <param name="baseAsset">The base (original) asset</param>
    /// <param name="targetAsset">The target (modified) asset</param>
    /// <returns>Complete diff result with all changes and summary</returns>
    DiffResult ComputeDiff(UAsset baseAsset, UAsset targetAsset);

    /// <summary>
    /// Gets changes at a specific path.
    /// </summary>
    DiffChange[] GetChangesForPath(DiffResult diff, string[] path);
}

/// <summary>
/// Core diff engine for comparing UAsset files.
/// </summary>
public sealed class DiffEngine : IDiffEngine
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Diff");

    private const double RenameConfidenceThreshold = 0.7;
    private const int MaxPropertyDepth = 20;

    private readonly IAppLogger _logger;
    private readonly RenameDetector _renameDetector;

    public DiffEngine(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _renameDetector = new RenameDetector();
    }

    public DiffResult ComputeDiff(UAsset baseAsset, UAsset targetAsset)
    {
        using var activity = ActivitySource.StartActivity("ComputeDiff");

        ArgumentNullException.ThrowIfNull(baseAsset);
        ArgumentNullException.ThrowIfNull(targetAsset);

        _logger.Info("Computing diff between assets");

        var changes = new List<DiffChange>();
        int unchanged = 0;

        // Compare exports
        var (exportChanges, exportUnchanged) = CompareExports(baseAsset, targetAsset);
        changes.AddRange(exportChanges);
        unchanged += exportUnchanged;

        // Compare imports
        var (importChanges, importUnchanged) = CompareImports(baseAsset, targetAsset);
        changes.AddRange(importChanges);
        unchanged += importUnchanged;

        // Compare name map
        var (nameChanges, nameUnchanged) = CompareNameMaps(baseAsset, targetAsset);
        changes.AddRange(nameChanges);
        unchanged += nameUnchanged;

        // Build summary
        var summary = BuildSummary(changes, unchanged);

        var result = new DiffResult(
            BaseVersion: baseAsset.FilePath ?? "base",
            TargetVersion: targetAsset.FilePath ?? "target",
            Changes: changes.ToArray(),
            Summary: summary
        );

        _logger.Info("Diff computed: {Added} added, {Removed} removed, {Modified} modified",
            summary.Added, summary.Removed, summary.Modified);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return result;
    }

    public DiffChange[] GetChangesForPath(DiffResult diff, string[] path)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(path);

        return diff.Changes
            .Where(c => PathMatches(c.Path, path))
            .ToArray();
    }

    private (List<DiffChange> changes, int unchanged) CompareExports(UAsset baseAsset, UAsset targetAsset)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        var baseExportNames = new Dictionary<string, int>();
        var targetExportNames = new Dictionary<string, int>();

        // Build lookup maps by export name
        for (int i = 0; i < baseAsset.Exports.Count; i++)
        {
            var name = baseAsset.Exports[i].ObjectName.Value.Value;
            baseExportNames[name] = i;
        }

        for (int i = 0; i < targetAsset.Exports.Count; i++)
        {
            var name = targetAsset.Exports[i].ObjectName.Value.Value;
            targetExportNames[name] = i;
        }

        // Find removed and modified exports
        foreach (var (name, baseIndex) in baseExportNames)
        {
            if (targetExportNames.TryGetValue(name, out int targetIndex))
            {
                // Export exists in both - compare properties
                var (propChanges, propUnchanged) = CompareExportProperties(
                    baseAsset, baseIndex,
                    targetAsset, targetIndex,
                    new[] { $"Export[{baseIndex}]" }
                );
                changes.AddRange(propChanges);
                unchanged += propUnchanged;
            }
            else
            {
                // Export removed
                changes.Add(new DiffChange(
                    Path: new[] { $"Export[{baseIndex}]", name },
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: GetExportSummary(baseAsset.Exports[baseIndex]),
                    NewValue: null
                ));
            }
        }

        // Find added exports
        foreach (var (name, targetIndex) in targetExportNames)
        {
            if (!baseExportNames.ContainsKey(name))
            {
                changes.Add(new DiffChange(
                    Path: new[] { $"Export[{targetIndex}]", name },
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: GetExportSummary(targetAsset.Exports[targetIndex])
                ));
            }
        }

        // Detect potential renames
        var removedExports = changes
            .Where(c => c.ChangeType == DiffChangeTypes.Removed)
            .Select(c => c.Path)
            .ToList();

        var addedExports = changes
            .Where(c => c.ChangeType == DiffChangeTypes.Added)
            .Select(c => c.Path)
            .ToList();

        if (removedExports.Count > 0 && addedExports.Count > 0)
        {
            var renames = _renameDetector.DetectRenames(
                removedExports.Select(p => p[^1]).ToArray(),
                addedExports.Select(p => p[^1]).ToArray(),
                name => GetExportPropertiesForName(baseAsset, name) ?? targetAsset
            );

            foreach (var (oldName, newName, confidence) in renames)
            {
                if (confidence >= RenameConfidenceThreshold)
                {
                    var oldPath = removedExports.FirstOrDefault(p => p[^1] == oldName);
                    var newPath = addedExports.FirstOrDefault(p => p[^1] == newName);

                    if (oldPath != null && newPath != null)
                    {
                        // Remove the added/removed entries
                        changes.RemoveAll(c =>
                            (c.ChangeType == DiffChangeTypes.Removed && c.Path.SequenceEqual(oldPath)) ||
                            (c.ChangeType == DiffChangeTypes.Added && c.Path.SequenceEqual(newPath)));

                        // Add rename entry
                        changes.Add(new DiffChange(
                            Path: newPath,
                            ChangeType: DiffChangeTypes.Renamed,
                            OldValue: oldName,
                            NewValue: newName,
                            Confidence: confidence,
                            OriginalPath: oldPath
                        ));
                    }
                }
            }
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareExportProperties(
        UAsset baseAsset, int baseExportIndex,
        UAsset targetAsset, int targetExportIndex,
        string[] parentPath)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        var baseExport = baseAsset.Exports[baseExportIndex];
        var targetExport = targetAsset.Exports[targetExportIndex];

        if (baseExport is not NormalExport baseNormal || targetExport is not NormalExport targetNormal)
        {
            // Compare as raw data
            if (baseExport is RawExport baseRaw && targetExport is RawExport targetRaw)
            {
                if (!baseRaw.Data.SequenceEqual(targetRaw.Data))
                {
                    changes.Add(new DiffChange(
                        Path: parentPath.Append("RawData").ToArray(),
                        ChangeType: DiffChangeTypes.Modified,
                        OldValue: $"{baseRaw.Data.Length} bytes",
                        NewValue: $"{targetRaw.Data.Length} bytes"
                    ));
                }
                else
                {
                    unchanged++;
                }
            }
            return (changes, unchanged);
        }

        // Build property maps
        var baseProps = new Dictionary<string, PropertyData>();
        var targetProps = new Dictionary<string, PropertyData>();

        foreach (var prop in baseNormal.Data)
        {
            baseProps[prop.Name.Value.Value] = prop;
        }

        foreach (var prop in targetNormal.Data)
        {
            targetProps[prop.Name.Value.Value] = prop;
        }

        // Compare properties
        foreach (var (name, baseProp) in baseProps)
        {
            var propPath = parentPath.Append(name).ToArray();

            if (targetProps.TryGetValue(name, out var targetProp))
            {
                var (propChanges, propUnchanged) = CompareProperties(
                    baseProp, targetProp, propPath, 0);
                changes.AddRange(propChanges);
                unchanged += propUnchanged;
            }
            else
            {
                changes.Add(new DiffChange(
                    Path: propPath,
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: GetPropertyValue(baseProp),
                    NewValue: null
                ));
            }
        }

        // Find added properties
        foreach (var (name, targetProp) in targetProps)
        {
            if (!baseProps.ContainsKey(name))
            {
                var propPath = parentPath.Append(name).ToArray();
                changes.Add(new DiffChange(
                    Path: propPath,
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: GetPropertyValue(targetProp)
                ));
            }
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareProperties(
        PropertyData baseProp,
        PropertyData targetProp,
        string[] path,
        int depth)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        if (depth > MaxPropertyDepth)
        {
            _logger.Warning("Max property depth exceeded at path: {Path}", string.Join("/", path));
            return (changes, unchanged);
        }

        // Type mismatch
        if (baseProp.PropertyType.Value != targetProp.PropertyType.Value)
        {
            changes.Add(new DiffChange(
                Path: path,
                ChangeType: DiffChangeTypes.Modified,
                OldValue: GetPropertyValue(baseProp),
                NewValue: GetPropertyValue(targetProp)
            ));
            return (changes, unchanged);
        }

        // Compare based on property type
        switch (baseProp)
        {
            case StructPropertyData baseStruct when targetProp is StructPropertyData targetStruct:
                return CompareStructProperties(baseStruct, targetStruct, path, depth);

            case ArrayPropertyData baseArray when targetProp is ArrayPropertyData targetArray:
                return CompareArrayProperties(baseArray, targetArray, path, depth);

            case MapPropertyData baseMap when targetProp is MapPropertyData targetMap:
                return CompareMapProperties(baseMap, targetMap, path, depth);

            default:
                // Compare scalar values
                if (!PropertyValuesEqual(baseProp, targetProp))
                {
                    changes.Add(new DiffChange(
                        Path: path,
                        ChangeType: DiffChangeTypes.Modified,
                        OldValue: GetPropertyValue(baseProp),
                        NewValue: GetPropertyValue(targetProp)
                    ));
                }
                else
                {
                    unchanged++;
                }
                break;
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareStructProperties(
        StructPropertyData baseStruct,
        StructPropertyData targetStruct,
        string[] path,
        int depth)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        var baseProps = baseStruct.Value.ToDictionary(p => p.Name.Value.Value);
        var targetProps = targetStruct.Value.ToDictionary(p => p.Name.Value.Value);

        foreach (var (name, baseProp) in baseProps)
        {
            var childPath = path.Append(name).ToArray();

            if (targetProps.TryGetValue(name, out var targetProp))
            {
                var (childChanges, childUnchanged) = CompareProperties(
                    baseProp, targetProp, childPath, depth + 1);
                changes.AddRange(childChanges);
                unchanged += childUnchanged;
            }
            else
            {
                changes.Add(new DiffChange(
                    Path: childPath,
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: GetPropertyValue(baseProp),
                    NewValue: null
                ));
            }
        }

        foreach (var (name, targetProp) in targetProps)
        {
            if (!baseProps.ContainsKey(name))
            {
                var childPath = path.Append(name).ToArray();
                changes.Add(new DiffChange(
                    Path: childPath,
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: GetPropertyValue(targetProp)
                ));
            }
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareArrayProperties(
        ArrayPropertyData baseArray,
        ArrayPropertyData targetArray,
        string[] path,
        int depth)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        int maxLength = Math.Max(baseArray.Value.Length, targetArray.Value.Length);

        for (int i = 0; i < maxLength; i++)
        {
            var elementPath = path.Append($"[{i}]").ToArray();

            if (i >= baseArray.Value.Length)
            {
                // Added element
                changes.Add(new DiffChange(
                    Path: elementPath,
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: GetPropertyValue(targetArray.Value[i])
                ));
            }
            else if (i >= targetArray.Value.Length)
            {
                // Removed element
                changes.Add(new DiffChange(
                    Path: elementPath,
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: GetPropertyValue(baseArray.Value[i]),
                    NewValue: null
                ));
            }
            else
            {
                // Compare elements
                var (elemChanges, elemUnchanged) = CompareProperties(
                    baseArray.Value[i], targetArray.Value[i], elementPath, depth + 1);
                changes.AddRange(elemChanges);
                unchanged += elemUnchanged;
            }
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareMapProperties(
        MapPropertyData baseMap,
        MapPropertyData targetMap,
        string[] path,
        int depth)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        // Build key lookups using string representation
        var baseEntries = baseMap.Value.ToDictionary(
            e => GetPropertyValueString(e.Key),
            e => e.Value
        );

        var targetEntries = targetMap.Value.ToDictionary(
            e => GetPropertyValueString(e.Key),
            e => e.Value
        );

        foreach (var (key, baseValue) in baseEntries)
        {
            var entryPath = path.Append($"[{key}]").ToArray();

            if (targetEntries.TryGetValue(key, out var targetValue))
            {
                var (entryChanges, entryUnchanged) = CompareProperties(
                    baseValue, targetValue, entryPath, depth + 1);
                changes.AddRange(entryChanges);
                unchanged += entryUnchanged;
            }
            else
            {
                changes.Add(new DiffChange(
                    Path: entryPath,
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: GetPropertyValue(baseValue),
                    NewValue: null
                ));
            }
        }

        foreach (var (key, targetValue) in targetEntries)
        {
            if (!baseEntries.ContainsKey(key))
            {
                var entryPath = path.Append($"[{key}]").ToArray();
                changes.Add(new DiffChange(
                    Path: entryPath,
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: GetPropertyValue(targetValue)
                ));
            }
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareImports(UAsset baseAsset, UAsset targetAsset)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        var baseImports = baseAsset.Imports.Select((i, idx) => (i, idx))
            .ToDictionary(x => x.i.ObjectName.Value.Value, x => x);

        var targetImports = targetAsset.Imports.Select((i, idx) => (i, idx))
            .ToDictionary(x => x.i.ObjectName.Value.Value, x => x);

        foreach (var (name, (baseImport, idx)) in baseImports)
        {
            if (!targetImports.ContainsKey(name))
            {
                changes.Add(new DiffChange(
                    Path: new[] { "Imports", $"[{idx}]", name },
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: baseImport.ClassName.Value.Value,
                    NewValue: null
                ));
            }
            else
            {
                unchanged++;
            }
        }

        foreach (var (name, (targetImport, idx)) in targetImports)
        {
            if (!baseImports.ContainsKey(name))
            {
                changes.Add(new DiffChange(
                    Path: new[] { "Imports", $"[{idx}]", name },
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: targetImport.ClassName.Value.Value
                ));
            }
        }

        return (changes, unchanged);
    }

    private (List<DiffChange> changes, int unchanged) CompareNameMaps(UAsset baseAsset, UAsset targetAsset)
    {
        var changes = new List<DiffChange>();
        int unchanged = 0;

        var baseNames = new HashSet<string>(baseAsset.GetNameMapIndexList().Select(n => n.Value));
        var targetNames = new HashSet<string>(targetAsset.GetNameMapIndexList().Select(n => n.Value));

        foreach (var name in baseNames)
        {
            if (!targetNames.Contains(name))
            {
                changes.Add(new DiffChange(
                    Path: new[] { "NameMap", name },
                    ChangeType: DiffChangeTypes.Removed,
                    OldValue: name,
                    NewValue: null
                ));
            }
            else
            {
                unchanged++;
            }
        }

        foreach (var name in targetNames)
        {
            if (!baseNames.Contains(name))
            {
                changes.Add(new DiffChange(
                    Path: new[] { "NameMap", name },
                    ChangeType: DiffChangeTypes.Added,
                    OldValue: null,
                    NewValue: name
                ));
            }
        }

        return (changes, unchanged);
    }

    private static bool PropertyValuesEqual(PropertyData a, PropertyData b)
    {
        return GetPropertyValueString(a) == GetPropertyValueString(b);
    }

    private static object? GetPropertyValue(PropertyData prop)
    {
        return prop switch
        {
            IntPropertyData p => p.Value,
            FloatPropertyData p => p.Value,
            DoublePropertyData p => p.Value,
            BoolPropertyData p => p.Value,
            StrPropertyData p => p.Value?.Value,
            NamePropertyData p => p.Value.Value.Value,
            BytePropertyData p => p.ByteType == BytePropertyType.Byte ? p.Value : p.EnumValue.Value.Value,
            EnumPropertyData p => p.Value.Value.Value,
            ObjectPropertyData p => p.Value.Index,
            SoftObjectPropertyData p => p.Value.AssetPath.AssetName.Value.Value,
            Int8PropertyData p => p.Value,
            Int16PropertyData p => p.Value,
            Int64PropertyData p => p.Value,
            UInt16PropertyData p => p.Value,
            UInt32PropertyData p => p.Value,
            UInt64PropertyData p => p.Value,
            ArrayPropertyData p => $"[{p.Value.Length} elements]",
            StructPropertyData p => $"({p.Value.Count} properties)",
            MapPropertyData p => $"{{{p.Value.Count} entries}}",
            _ => prop.ToString()
        };
    }

    private static string GetPropertyValueString(PropertyData prop)
    {
        var value = GetPropertyValue(prop);
        return value?.ToString() ?? "null";
    }

    private static string? GetExportSummary(Export export)
    {
        var name = export.ObjectName.Value.Value;
        if (export is NormalExport normal)
        {
            return $"{name} ({normal.Data.Count} properties)";
        }
        if (export is RawExport raw)
        {
            return $"{name} ({raw.Data.Length} bytes)";
        }
        return name;
    }

    private static object? GetExportPropertiesForName(UAsset asset, string name)
    {
        var export = asset.Exports.FirstOrDefault(e => e.ObjectName.Value.Value == name);
        return export is NormalExport normal ? normal.Data : null;
    }

    private static DiffSummary BuildSummary(List<DiffChange> changes, int unchanged)
    {
        return new DiffSummary(
            Added: changes.Count(c => c.ChangeType == DiffChangeTypes.Added),
            Removed: changes.Count(c => c.ChangeType == DiffChangeTypes.Removed),
            Modified: changes.Count(c => c.ChangeType == DiffChangeTypes.Modified),
            Unchanged: unchanged,
            Renamed: changes.Count(c => c.ChangeType == DiffChangeTypes.Renamed)
        );
    }

    private static bool PathMatches(string[] path, string[] filter)
    {
        if (filter.Length > path.Length)
        {
            return false;
        }

        for (int i = 0; i < filter.Length; i++)
        {
            if (path[i] != filter[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Detects renames between removed and added items by comparing property signatures.
/// </summary>
internal sealed class RenameDetector
{
    public (string oldName, string newName, double confidence)[] DetectRenames(
        string[] removedNames,
        string[] addedNames,
        Func<string, object?> getProperties)
    {
        var results = new List<(string, string, double)>();

        foreach (var oldName in removedNames)
        {
            var oldProps = getProperties(oldName);
            if (oldProps == null) continue;

            double bestScore = 0;
            string? bestMatch = null;

            foreach (var newName in addedNames)
            {
                var newProps = getProperties(newName);
                if (newProps == null) continue;

                double score = CalculateSimilarity(oldName, newName, oldProps, newProps);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = newName;
                }
            }

            if (bestMatch != null && bestScore > 0.5)
            {
                results.Add((oldName, bestMatch, bestScore));
            }
        }

        return results.ToArray();
    }

    private static double CalculateSimilarity(
        string oldName,
        string newName,
        object oldProps,
        object newProps)
    {
        double nameSimilarity = CalculateStringSimilarity(oldName, newName);

        // Property structure similarity
        double propSimilarity = 0;
        if (oldProps is IList<PropertyData> oldList && newProps is IList<PropertyData> newList)
        {
            var oldPropNames = new HashSet<string>(oldList.Select(p => p.Name.Value.Value));
            var newPropNames = new HashSet<string>(newList.Select(p => p.Name.Value.Value));

            int intersection = oldPropNames.Intersect(newPropNames).Count();
            int union = oldPropNames.Union(newPropNames).Count();

            propSimilarity = union > 0 ? (double)intersection / union : 0;
        }

        // Weighted average
        return (nameSimilarity * 0.3) + (propSimilarity * 0.7);
    }

    private static double CalculateStringSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0;
        }

        int maxLen = Math.Max(a.Length, b.Length);
        int distance = LevenshteinDistance(a, b);

        return 1.0 - ((double)distance / maxLen);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int[,] dp = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost
                );
            }
        }

        return dp[a.Length, b.Length];
    }
}
