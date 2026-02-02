# Bridge

## Purpose

This directory contains the **IPC bridge layer** that handles all communication between the Svelte frontend and the C# backend. The TypeScript types here are the **immutable shared contracts** that mirror the C# models in `godot/scripts/Models/`.

**CRITICAL**: These contracts are immutable once defined. All agents MUST use these exact types. Breaking changes require coordination with ALL agents.

## Contents

| File | Description |
| ---- | ----------- |
| `types.ts` | **Shared contracts** - All IPC message types and data structures |
| `ipc.ts` | IPC wrapper for sending/receiving messages (created by 02-frontend) |

## Design Decisions

- **Single Source of Truth**: `types.ts` defines all types that both frontend and backend use
- **Explicit Type Unions**: Using string literal unions instead of enums for better TypeScript type inference
- **Branded Types**: `IPC_PREFIX` and `IPC_RECEIVER` are const strings for message identification
- **Comprehensive Types**: All possible IPC scenarios are typed upfront to prevent runtime errors
- **Three-Way Diff Support**: Includes types for the mod porting workflow described in ARCHITECTURE.md

## Dependencies

### Internal

- None (this is a foundational module)

### External

- None (pure TypeScript types)

## Usage Examples

### Importing Types

```typescript
import type {
    IpcMessage,
    TreeNode,
    PropertyValue,
    DiffResult
} from '$lib/bridge/types';
```

### Creating Messages (via ipc.ts)

```typescript
import { ipc } from '$lib/bridge/ipc';

// Send a selection action to C#
ipc.send({
    type: 'selection',
    action: 'select',
    payload: { nodeId: 'export[0]' }
});
```

### Handling Responses

```typescript
import type { IpcMessage, ErrorResponse } from '$lib/bridge/types';

function handleMessage(msg: IpcMessage) {
    if (msg.type === 'error') {
        const error = msg.payload as ErrorResponse;
        console.error(`Error ${error.code}: ${error.message}`);
    }
}
```

### Type Guards

```typescript
import type { TreeNode, TreeNodeType } from '$lib/bridge/types';

function isExportNode(node: TreeNode): boolean {
    return node.type === 'export';
}
```

## Contract Versioning

These types are versioned implicitly through the git history. Once the 00-shared-contracts agent completes:

1. Types cannot be modified without a sync meeting
2. New types can be appended (additive changes are safe)
3. Existing type shapes are frozen
4. All agents must use the exact types defined here

## Agent Ownership

**Owner**: 00-shared-contracts (types.ts only)

The `types.ts` file is owned by the Shared Contracts agent and is **immutable** once complete.

The `ipc.ts` file will be created by the 02-frontend agent.

## Related Documentation

- [Models/](../../../../godot/scripts/Models/) - C# mirror of these contracts
- [ARCHITECTURE.md](../../../../plans/ARCHITECTURE.md) - IPC protocol specification
- [00-shared-contracts.md](../../../../plans/00-shared-contracts.md) - Contract agent specification
