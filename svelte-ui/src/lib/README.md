# Frontend Library

## Purpose
Reusable frontend modules shared across the Svelte UI, including typed IPC contracts, view models, constants, and component libraries.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `bridge/` | IPC bridge wrapper and shared TypeScript contracts. |
| `components/` | Reusable presentation components. |
| `view-models/` | Backend-fed view models and UI-local transient state. |
| `constants.ts` | Shared UI constants and labels. |

## Problem
The frontend needs a shared library layer that separates transport, state projection, and rendering concerns.

## Constraints
- Shared contracts must stay aligned with the backend.
- Components should not become the source of truth for backend-owned data.

## Decision
Keep the reusable frontend library under `src/lib` with explicit bridge, view-model, and component boundaries.

## Alternatives Rejected
- Put all frontend code directly under route files: rejected because contract and component reuse would become harder to maintain.

## Invariants
- Typed bridge contracts stay centralized.
- View models remain the presentation-facing cache for backend-owned state.

## Revisit Triggers
- The frontend adopts a different state-management model.
- Shared components or contracts move into separate packages.

## Dependencies
**Internal:** `svelte-ui/src`.
**External:** Svelte, TypeScript.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: the current library split is still local to the frontend.
- Revisit trigger: frontend modules are extracted into packages.

## Usage Examples
```typescript
import { ipc } from '$lib/bridge/ipc';
```

## API Consumer Contract
- Frontend routes/components are the intended consumers.
- Consumers should use typed bridge and view-model APIs instead of mutating backend-owned state directly.

## Structured Producer Contract
- `bridge/types.ts` is the primary structured contract producer under this library.
- Contract changes must stay compatible with backend models or land atomically on both sides.
