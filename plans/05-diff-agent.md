# Diff Agent

**Phase**: 2 - Features
**Depends on**: Asset Agent (04), Frontend Agent (02)

## Scope

Diff engine, conflict detection, mod porting workflow.

## Purpose

Enable modders to update their mods when games update:
1. Compare original game asset (v1.0) with updated (v1.1)
2. Compare original (v1.0) with modded version
3. Detect conflicts where both mod and game changed same property
4. Generate patches to apply mod changes to new base

## Files to Create

```
godot/scripts/
├── Diff/
│   ├── DiffEngine.cs          # Core diff algorithm
│   ├── ConflictDetector.cs    # Mod vs game changes
│   ├── PatchGenerator.cs      # Generate update patches
│   ├── PatchApplier.cs        # Apply patches to assets
│   └── README.md

svelte-ui/src/lib/
├── components/diff/
│   ├── DiffView.svelte        # Side-by-side view
│   ├── DiffHighlight.svelte   # Change highlighting
│   ├── DiffTree.svelte        # Tree with diff markers
│   └── ConflictPanel.svelte   # Conflict resolution UI
└── view-models/
    └── diff.svelte.ts         # (extend existing)
```

## Tasks

### 1. Diff Engine

```csharp
public interface IDiffEngine
{
    DiffResult ComputeDiff(UAsset baseAsset, UAsset targetAsset);
    DiffChange[] GetChangesForPath(DiffResult diff, string[] path);
}

public class DiffEngine : IDiffEngine
{
    public DiffResult ComputeDiff(UAsset baseAsset, UAsset targetAsset)
    {
        // Compare exports
        // Compare properties recursively
        // Detect renames/moves
        // Build change list
    }
}
```

- [ ] Compare export lists
- [ ] Compare property trees recursively
- [ ] Detect added/removed/modified
- [ ] Detect renames with confidence score
- [ ] Generate summary statistics

### 2. Tree Comparison Algorithm

```csharp
private DiffChange[] CompareNodes(TreeNode baseNode, TreeNode targetNode)
{
    var changes = new List<DiffChange>();

    // Compare properties
    foreach (var baseProp in baseNode.Properties)
    {
        var targetProp = targetNode.Properties.FirstOrDefault(p => p.Name == baseProp.Name);

        if (targetProp == null)
            changes.Add(new DiffChange(baseProp.Path, "removed", baseProp.Value, null));
        else if (!Equals(baseProp.Value, targetProp.Value))
            changes.Add(new DiffChange(baseProp.Path, "modified", baseProp.Value, targetProp.Value));
    }

    // Find added properties
    foreach (var targetProp in targetNode.Properties)
    {
        if (!baseNode.Properties.Any(p => p.Name == targetProp.Name))
            changes.Add(new DiffChange(targetProp.Path, "added", null, targetProp.Value));
    }

    // Recurse into children
    // ...

    return changes.ToArray();
}
```

- [ ] Implement recursive comparison
- [ ] Handle arrays (index changes)
- [ ] Handle nested structs
- [ ] Handle object references

### 3. Rename Detection

```csharp
public class RenameDetector
{
    public (string oldName, string newName, double confidence)[] DetectRenames(
        string[] removedNames,
        string[] addedNames,
        Func<string, object> getProperties)
    {
        // Compare property signatures to detect renames
        // Return matches with confidence scores
    }
}
```

- [ ] Compare property signatures
- [ ] Use fuzzy matching for names
- [ ] Calculate confidence score
- [ ] Flag low-confidence matches for review

### 4. Conflict Detector

```csharp
public interface IConflictDetector
{
    ConflictResult DetectConflicts(
        DiffResult gameChanges,   // v1.0 → v1.1
        DiffResult modChanges);   // v1.0 → mod
}

public record ConflictResult(
    DiffChange[] NonConflicting,  // Mod changes game didn't touch
    DiffChange[] Conflicting,     // Both changed same property
    DiffChange[] Structural       // Game removed something mod depends on
);
```

- [ ] Identify non-conflicting changes
- [ ] Identify conflicting changes
- [ ] Identify structural breaks
- [ ] Suggest resolutions

### 5. Patch Generator

```csharp
public interface IPatchGenerator
{
    Patch[] GeneratePatches(DiffResult modChanges, ConflictResult conflicts);
}

public record Patch(
    string[] Path,
    PatchOperation Operation,  // Set, Add, Remove
    object? Value,
    bool RequiresReview        // True if conflicting
);
```

- [ ] Generate patches for non-conflicts
- [ ] Flag conflicts for review
- [ ] Support all property types

### 6. Patch Applier

```csharp
public interface IPatchApplier
{
    ApplyResult ApplyPatches(UAsset baseAsset, Patch[] patches);
}

public record ApplyResult(
    int Applied,
    int Skipped,
    string[] Errors
);
```

- [ ] Apply set operations
- [ ] Apply add operations
- [ ] Apply remove operations
- [ ] Handle errors gracefully

### 7. IPC Handler

```csharp
public class DiffHandler : IMessageHandler
{
    public async Task<object> Handle(IpcMessage message)
    {
        return message.Action switch
        {
            "compare" => await CompareTwoAssets(message.Payload),
            "detectConflicts" => await DetectModConflicts(message.Payload),
            "generatePatches" => await GeneratePatches(message.Payload),
            "applyPatches" => await ApplyPatches(message.Payload),
            _ => throw new NotSupportedException()
        };
    }
}
```

- [ ] Implement diff handler
- [ ] Register with dispatcher

### 8. Frontend Components

**DiffView.svelte**:
```svelte
<script lang="ts">
  import { ipc } from '../bridge/ipc';
  import type { DiffResult, DiffChange } from '../bridge/types';

  let diffResult = $state<DiffResult | null>(null);

  ipc.on('diff', (data) => { diffResult = data as DiffResult; });

  function scrollToChange(change: DiffChange) {
    ipc.send({ type: 'diff', action: 'scrollTo', payload: { path: change.path } });
  }
</script>

<div class="diff-view">
  <div class="diff-panel left">
    <!-- Base version tree -->
  </div>
  <div class="diff-panel right">
    <!-- Target version tree -->
  </div>
</div>
```

- [ ] Create side-by-side layout
- [ ] Highlight changes with diff colors
- [ ] Sync scroll between panels
- [ ] Show change markers in tree

**ConflictPanel.svelte**:
- [ ] List conflicting changes
- [ ] Show old/new values
- [ ] Allow user to choose resolution
- [ ] Forward resolution to C#

### 9. View Model Extension

```typescript
// view-models/diff.svelte.ts
export let diffResult = $state<DiffResult | null>(null);
export let conflicts = $state<ConflictResult | null>(null);

ipc.on('diff', (data) => { diffResult = data; });
ipc.on('conflicts', (data) => { conflicts = data; });

export function compare(pathA: string, pathB: string) {
    ipc.send({ type: 'diff', action: 'compare', payload: { pathA, pathB } });
}

export function resolveConflict(path: string[], resolution: 'keepMod' | 'keepGame' | 'custom', value?: unknown) {
    ipc.send({ type: 'diff', action: 'resolve', payload: { path, resolution, value } });
}
```

- [ ] Extend diff view model
- [ ] Handle conflict resolution

## Diff Highlighting Colors

```css
--diff-added: #22c55e;      /* Green */
--diff-removed: #ef4444;    /* Red */
--diff-modified: #f59e0b;   /* Amber */
--diff-moved: #3b82f6;      /* Blue */
--diff-conflict: #c678dd;   /* Purple */
```

## Testing

- [ ] Unit test: Compare identical assets → no changes
- [ ] Unit test: Compare with added property
- [ ] Unit test: Compare with removed property
- [ ] Unit test: Compare with modified property
- [ ] Unit test: Detect rename
- [ ] Integration test: Full mod porting workflow

## Outputs for Other Agents

1. **DiffEngine** - AI Agent uses for comparisons
2. **ConflictDetector** - AI Agent uses for mod porting
3. **UI Components** - Frontend displays diff results

## Acceptance Criteria

- [ ] Can compare two assets
- [ ] Changes displayed with correct colors
- [ ] Conflicts detected correctly
- [ ] Patches generated for non-conflicts
- [ ] Patches can be applied
- [ ] UI shows side-by-side diff
- [ ] Conflicts can be resolved manually
- [ ] All operations traced

## Workflow Example

```
1. User loads: original_v1.0.uasset, updated_v1.1.uasset, modded.uasset
2. System computes:
   - gameChanges = diff(v1.0, v1.1)
   - modChanges = diff(v1.0, modded)
3. System detects conflicts
4. UI shows:
   - Green: Mod changes to apply automatically
   - Purple: Conflicts needing resolution
   - Red: Broken (structural changes)
5. User resolves conflicts
6. System applies patches to v1.1 base
7. User saves updated mod
```
