# Shared Contracts Agent

**Priority**: Must be completed BEFORE all other workstreams begin

## Purpose

Define the contracts that all parallel agents will use. These contracts become immutable once approved.

## Deliverables

### 1. IPC Message Types

Create `svelte-ui/src/lib/bridge/types.ts`:

```typescript
// All agents reference this contract
export type MessageType =
    | 'asset' | 'tree' | 'property' | 'selection'
    | 'diff' | 'viewport' | 'agent' | 'error';

export interface IpcMessage {
    type: MessageType;
    action: string;
    payload: unknown;
    id?: string;  // For request/response correlation
}

// Tree types
export interface TreeNode {
    id: string;
    name: string;
    type: string;
    hasChildren: boolean;
    children?: TreeNode[];
}

// Property types
export interface PropertyValue {
    path: string[];
    type: string;
    value: unknown;
    editable: boolean;
}

// Selection types
export interface SelectionState {
    selectedId: string | null;
    expandedIds: string[];
}

// Diff types
export interface DiffChange {
    path: string[];
    changeType: 'added' | 'removed' | 'modified' | 'renamed';
    oldValue?: unknown;
    newValue?: unknown;
    confidence?: number;
}

export interface DiffResult {
    baseVersion: string;
    targetVersion: string;
    changes: DiffChange[];
    summary: {
        added: number;
        removed: number;
        modified: number;
        unchanged: number;
    };
}

// Agent types
export interface AgentMessage {
    agentId: string;
    status: 'thinking' | 'executing' | 'complete' | 'error';
    message: string;
    progress?: number;
}

// Error types
export interface ErrorResponse {
    code: string;
    message: string;
    details?: unknown;
}
```

### 2. C# Data Models

Create `godot/scripts/Models/` directory with:

**TreeNode.cs**:
```csharp
namespace UAssetViewer.Models;

public record TreeNode(
    string Id,
    string Name,
    string Type,
    bool HasChildren
);
```

**PropertyValue.cs**:
```csharp
namespace UAssetViewer.Models;

public record PropertyValue(
    string[] Path,
    string Type,
    object Value,
    bool Editable
);
```

**DiffResult.cs**:
```csharp
namespace UAssetViewer.Models;

public record DiffChange(
    string[] Path,
    string ChangeType,
    object? OldValue,
    object? NewValue,
    double? Confidence = null
);

public record DiffSummary(
    int Added,
    int Removed,
    int Modified,
    int Unchanged
);

public record DiffResult(
    string BaseVersion,
    string TargetVersion,
    DiffChange[] Changes,
    DiffSummary Summary
);
```

**IpcMessage.cs**:
```csharp
namespace UAssetViewer.Models;

public record IpcMessage(
    string Type,
    string Action,
    object Payload,
    string? Id = null
);
```

### 3. Directory Structure Validation

Create script to validate directory structure matches ARCHITECTURE.md.

### 4. README Template

Create template for directory README files:

```markdown
# {Directory Name}

## Purpose
{Brief description}

## Contents
- `{file}` - {description}

## Design Decisions
- **{Decision}**: {Reasoning}

## Dependencies
- Internal: {list}
- External: {list}

## Usage Examples
\`\`\`{language}
{code}
\`\`\`
```

## Tasks

1. [ ] Create `svelte-ui/src/lib/bridge/types.ts` with all shared types
2. [ ] Create `godot/scripts/Models/` directory
3. [ ] Create all C# model records
4. [ ] Create README template file
5. [ ] Create directory validation script
6. [ ] Document any decisions made during contract definition

## Acceptance Criteria

- All TypeScript types compile without errors
- All C# records compile without errors
- Types can be serialized/deserialized via System.Text.Json
- Other agents can import and use these types
- README template is clear and complete

## Immutability Rule

Once this workstream is complete and approved:
- These contracts CANNOT be changed without a sync meeting
- All agents MUST use these exact types
- Breaking changes require coordination with ALL agents
