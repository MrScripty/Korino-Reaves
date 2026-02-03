# Diff Engine

Asset comparison and mod porting workflow for UAsset files.

## Purpose

Enables modders to update their mods when games update by:
1. Comparing original game asset (v1.0) with updated version (v1.1)
2. Comparing original (v1.0) with modded version
3. Detecting conflicts where both mod and game changed the same property
4. Generating patches to apply mod changes to the new base

## Contents

- `DiffEngine.cs` - Core diff algorithm for comparing two assets
- `ConflictDetector.cs` - Three-way diff for detecting mod vs game conflicts
- `PatchGenerator.cs` - Generates patches from diff results
- `PatchApplier.cs` - Applies patches to update assets
- `DiffHandler.cs` - IPC handler for frontend communication

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          DiffHandler (IPC)                               │
│  Receives frontend requests, coordinates operations, sends responses    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────┐  │
│  │   DiffEngine    │  │ ConflictDetector│  │     PatchGenerator      │  │
│  │                 │  │                 │  │                         │  │
│  │ • 2-way diff    │──│ • 3-way diff    │──│ • Create patches        │  │
│  │ • Compare       │  │ • Find conflicts│  │ • Mark review items     │  │
│  │   properties    │  │ • Safe changes  │  │                         │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────┘  │
│                                                    │                     │
│                                                    ▼                     │
│                                          ┌─────────────────────────┐    │
│                                          │     PatchApplier        │    │
│                                          │                         │    │
│                                          │ • Apply patches         │    │
│                                          │ • Resolve conflicts     │    │
│                                          └─────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

## Design Decisions

### Path-Based Change Tracking
Changes are tracked using array paths (e.g., `["Export[0]", "Health"]`) which:
- Allow precise navigation in the UI
- Support nested properties and arrays
- Enable conflict detection at any depth

### Two-Stage Patch Application
1. **Generation**: Patches created with `requiresReview` flag for conflicts
2. **Application**: Safe patches auto-applied, conflicts require user decision

### Rename Detection
Uses property signature similarity to detect renames:
- Compares property sets between removed/added items
- Uses Levenshtein distance for name similarity
- Requires 70% confidence threshold

## Dependencies

### Internal
- `Models/DiffResult.cs` - Data structures for diff results
- `Assets/AssetLoader.cs` - Asset loading for comparisons
- `Bridge/IMessageHandler.cs` - IPC handler interface

### External
- UAssetAPI - Asset parsing and property access

## Usage Examples

### Two-Way Comparison
```csharp
var diffEngine = new DiffEngine(logger);
var result = diffEngine.ComputeDiff(baseAsset, targetAsset);
// result.Changes contains all differences
// result.Summary has counts (added, removed, modified)
```

### Three-Way Mod Porting
```csharp
var conflictDetector = new ConflictDetector(logger, diffEngine);
var result = conflictDetector.PerformThreeWayDiff(
    original,   // v1.0 game asset
    updated,    // v1.1 game asset
    modded      // mod based on v1.0
);
// result.SafeToApply - changes that don't conflict
// result.Conflicts - changes needing resolution
```

### Apply Safe Changes
```csharp
var patchApplier = new PatchApplier(logger);
var result = patchApplier.ApplySafeChanges(targetAsset, threeWayResult);
// result.Applied - count of successful patches
// result.Failed - count of failed patches
```

## IPC Actions

| Action | Request Payload | Response Action |
|--------|-----------------|-----------------|
| `compare` | `{ basePath, targetPath }` | `result` |
| `threeWayCompare` | `{ originalPath, updatedPath, moddedPath }` | `threeWayResult` |
| `applySafe` | `{}` | `safeApplied` |
| `resolveConflict` | `{ path[], resolution, customValue? }` | `conflictResolved` |
| `clear` | `{}` | `clear` |
| `generatePatches` | `{}` | `patchesGenerated` |
| `navigateTo` | `{ path[] }` | (selection action) |

## Diff Colors

The frontend uses these CSS variables for diff highlighting:

| Change Type | CSS Variable | Color |
|-------------|--------------|-------|
| Added | `--diff-added` | Green (#22c55e) |
| Removed | `--diff-removed` | Red (#ef4444) |
| Modified | `--diff-modified` | Amber (#f59e0b) |
| Renamed/Moved | `--diff-moved` | Blue (#3b82f6) |
| Conflict | `--diff-conflict` | Purple (#c678dd) |
