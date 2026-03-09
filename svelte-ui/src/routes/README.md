# Routes

## Purpose
Top-level SvelteKit route modules for application layout and the main page.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `+layout.svelte` | Shared app layout wrapper. |
| `+layout.ts` | Layout-level load/config wiring. |
| `+page.svelte` | Main workspace route. |

## Problem
The frontend needs a route entry point that assembles the docked workspace, layout shell, and global providers.

## Constraints
- The app currently behaves like a single-workspace application inside embedded CEF.

## Decision
Keep route-level composition thin and push reusable logic into `src/lib`.

## Alternatives Rejected
- Put most app logic directly in route files: rejected because it would blur routing and reusable UI/state responsibilities.

## Invariants
- Route modules remain the entry point, not the primary location for reusable logic.
- Shared components and view models live under `src/lib`.

## Revisit Triggers
- Additional routes or route-level loading concerns are introduced.

## Dependencies
**Internal:** `svelte-ui/src/lib`.
**External:** SvelteKit.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: current routing is simple and local to the frontend.
- Revisit trigger: multi-route workflows or route guards appear.

## Usage Examples
```svelte
<!-- +page.svelte hosts the main workspace -->
```

## API Consumer Contract
- SvelteKit is the immediate consumer of these route modules.
- Route modules should remain thin composition layers.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: route modules do not publish persisted structured artifacts.
- Revisit trigger: route metadata or manifests become a checked-in contract.
