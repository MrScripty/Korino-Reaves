# Log Components

## Purpose
Components for rendering runtime log output inside the UI.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `LogPanel.svelte` | Scrollable log viewer panel. |

## Problem
Users need in-app visibility into runtime events and failures without leaving the tool.

## Constraints
- Log volume can grow quickly.
- The panel should remain read-only from the UI perspective.

## Decision
Keep log rendering isolated in a dedicated component tied to the log view model.

## Alternatives Rejected
- Show logs only in stdout/files: rejected because embedded UI workflows benefit from in-app visibility.

## Invariants
- Log entries are consumed from backend-driven events.
- The panel stays presentation-focused and read-only.

## Revisit Triggers
- Filtering/searching requires additional log components.

## Dependencies
**Internal:** log view model and common virtual-list utilities.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a small presentation boundary.
- Revisit trigger: logging UX becomes significantly richer.

## Usage Examples
```svelte
<LogPanel />
```

## API Consumer Contract
- Parent layouts host the panel.
- Log payload shape remains the source of truth from the runtime bridge.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the panel consumes log events but does not publish persisted schemas.
- Revisit trigger: log export or saved log filters become a supported artifact.
