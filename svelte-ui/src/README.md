# Svelte UI Source

## Purpose
Application shell, route definitions, and top-level styling for the Svelte frontend.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `app.css` | Global visual tokens and baseline styling. |
| `app.html` | SvelteKit host document. |
| `hooks.ts` | App-level request/response hook wiring. |
| `lib/` | Typed bridge, view models, and reusable UI components. |
| `routes/` | Route entry points and layout/page composition. |

## Problem
The frontend needs a typed application shell that can render backend-owned state and route UI interactions back through IPC.

## Constraints
- The UI runs inside embedded CEF and also supports local dev-server workflows.
- Frontend state must stay aligned with backend-owned contracts.

## Decision
Keep app shell, routes, and reusable modules under `src/` with bridge/view-model boundaries under `lib/`.

## Alternatives Rejected
- Collapse all frontend code into a single route module: rejected because the UI has distinct bridge, component, and route responsibilities.

## Invariants
- The frontend treats backend data as the source of truth for shared state.
- Route composition stays separate from reusable components and view models.

## Revisit Triggers
- The frontend outgrows a single-route app shell.
- Another UI runtime replaces SvelteKit/CEF.

## Dependencies
**Internal:** `svelte-ui/src/lib`.
**External:** Svelte, SvelteKit, TypeScript.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: frontend architecture is currently documented through module READMEs instead.
- Revisit trigger: routing or state ownership changes materially.

## Usage Examples
```bash
cd svelte-ui
npm run check
```

## API Consumer Contract
- The embedded browser and local dev workflow are the intended consumers.
- Routes and components consume typed bridge/view-model data rather than ad hoc global state.

## Structured Producer Contract
- `app.html`, route modules, and build output stay compatible with the embedded browser host.
- Any persisted frontend schema should be documented under the producing subdirectory when added.
